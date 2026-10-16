using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace HwMonitor.Sensors;

public enum HwInfoReadingType { None, Temp, Volt, Fan, Current, Power, Clock, Usage, Other }

/// <summary>One HWiNFO value. <c>*Orig</c> are HWiNFO's English names (for matching), the others can be renamed by the user.</summary>
public sealed record HwInfoReading(HwInfoReadingType Type, uint Id, string LabelOrig, string Label, string Unit, double Value);

/// <summary>One HWiNFO sensor group, e.g. "CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced".</summary>
public sealed record HwInfoSensor(uint Id, uint Instance, string NameOrig, string Name, IReadOnlyList<HwInfoReading> Readings);

/// <summary>
/// Reads the HWiNFO shared memory (HWiNFO setting "Shared Memory Support").
/// Layout from the HWiNFO SDK (packed structs); element sizes come from the header, because they grow between versions.
/// </summary>
public sealed class HwInfoSharedMemory(TimeProvider time, ILogger logger) : IDisposable
{
    private const string MapName = @"Global\HWiNFO_SENS_SM2";
    private const string MutexName = @"Global\HWiNFO_SM2_MUTEX";
    private const uint Signature = 0x53695748; // "HWiS"; "DEAD" when HWiNFO turned it off

    private const int HeaderSize = 44;
    private const int NameSize = 128;
    private const int UnitSize = 16;
    // Sensor: id, instance, nameOrig, nameUser (+ utf8 nameUser in v2).
    private const int SensorSize = 8 + 2 * NameSize;
    private const int SensorSizeUtf8 = SensorSize + NameSize;
    // Reading: type, sensorIndex, id, labelOrig, labelUser, unit, value, min, max, avg (+ utf8 labelUser, utf8 unit in v2).
    private const int ReadingSize = 12 + 2 * NameSize + UnitSize + 4 * 8;
    private const int ReadingSizeUtf8 = ReadingSize + NameSize + UnitSize;

    // HWiNFO polls every 2 s by default; older data means it stopped (closed, or the free 12-hour limit).
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);

    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private Mutex? _mutex;
    private byte[] _buffer = [];
    private string? _lastProblem;

    /// <summary>All sensor groups, or null when HWiNFO shared memory is not there or not updated.</summary>
    public IReadOnlyList<HwInfoSensor>? TryRead()
    {
        if (_view is null && !TryOpen())
            return null;

        if (Copy() is not { } data)
        {
            // An open handle keeps the old memory alive; a new HWiNFO could then get the old (too small) block.
            Close();
            return null;
        }
        _lastProblem = null;
        return data;
    }

    private bool TryOpen()
    {
        try
        {
            _file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }
        catch (Exception e) when (e is FileNotFoundException or UnauthorizedAccessException or IOException)
        {
            if (e is not FileNotFoundException)
                Problem($"Cannot open HWiNFO shared memory: {e.Message}");
            Close();
            return false;
        }

        try
        {
            _mutex = Mutex.TryOpenExisting(MutexName, out var mutex) ? mutex : null;
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex is optional; HWiNFO writes the values in place, so a read without it is still fine.
        }
        return true;
    }

    private List<HwInfoSensor>? Copy()
    {
        var view = _view!;
        var capacity = view.Capacity;
        if (capacity < HeaderSize)
            return Problem("HWiNFO shared memory is too small");

        var locked = Lock();
        try
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            for (var i = 0; i < HeaderSize; i++)
                header[i] = view.ReadByte(i);

            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Signature)
                return null;
            var pollTime = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(header[12..]));
            if (time.GetUtcNow() - pollTime > MaxAge)
                return null;

            var sensorOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
            var sensorSize = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            var sensorCount = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
            var readingOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
            var readingSize = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
            var readingCount = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);

            // A wrong layout would give wrong numbers without any error, so stop instead.
            if (sensorSize < SensorSize || readingSize < ReadingSize)
                return Problem($"Unknown HWiNFO shared memory layout (sensor {sensorSize} B, reading {readingSize} B)");
            var end = Math.Max(sensorOffset + (long)sensorSize * sensorCount, readingOffset + (long)readingSize * readingCount);
            if (end > capacity)
                return Problem("HWiNFO shared memory is smaller than its header says");

            if (_buffer.Length < end)
                _buffer = new byte[end];
            view.ReadArray(0, _buffer, 0, (int)end);

            return Parse(_buffer, (int)sensorOffset, (int)sensorSize, (int)sensorCount,
                (int)readingOffset, (int)readingSize, (int)readingCount);
        }
        finally
        {
            if (locked)
                _mutex!.ReleaseMutex();
        }
    }

    private static List<HwInfoSensor> Parse(byte[] buffer, int sensorOffset, int sensorSize, int sensorCount,
        int readingOffset, int readingSize, int readingCount)
    {
        var readings = new List<HwInfoReading>[sensorCount];
        for (var i = 0; i < sensorCount; i++)
            readings[i] = [];

        var utf8Readings = readingSize >= ReadingSizeUtf8;
        for (var i = 0; i < readingCount; i++)
        {
            var r = buffer.AsSpan(readingOffset + i * readingSize, readingSize);
            var sensorIndex = BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
            if (sensorIndex >= sensorCount)
                continue;
            var labelOrig = Ansi(r.Slice(12, NameSize));
            var label = utf8Readings ? Utf8(r.Slice(ReadingSize, NameSize)) : Ansi(r.Slice(12 + NameSize, NameSize));
            var unit = utf8Readings ? Utf8(r.Slice(ReadingSize + NameSize, UnitSize)) : Ansi(r.Slice(12 + 2 * NameSize, UnitSize));
            readings[sensorIndex].Add(new HwInfoReading(
                (HwInfoReadingType)BinaryPrimitives.ReadUInt32LittleEndian(r),
                BinaryPrimitives.ReadUInt32LittleEndian(r[8..]),
                labelOrig,
                label.Length > 0 ? label : labelOrig,
                unit,
                BinaryPrimitives.ReadDoubleLittleEndian(r[(12 + 2 * NameSize + UnitSize)..])));
        }

        var utf8Sensors = sensorSize >= SensorSizeUtf8;
        var sensors = new List<HwInfoSensor>(sensorCount);
        for (var i = 0; i < sensorCount; i++)
        {
            var s = buffer.AsSpan(sensorOffset + i * sensorSize, sensorSize);
            var nameOrig = Ansi(s.Slice(8, NameSize));
            var name = utf8Sensors ? Utf8(s.Slice(SensorSize, NameSize)) : Ansi(s.Slice(8 + NameSize, NameSize));
            sensors.Add(new HwInfoSensor(
                BinaryPrimitives.ReadUInt32LittleEndian(s),
                BinaryPrimitives.ReadUInt32LittleEndian(s[4..]),
                nameOrig,
                name.Length > 0 ? name : nameOrig,
                readings[i]));
        }
        return sensors;
    }

    private bool Lock()
    {
        if (_mutex is null)
            return false;
        try
        {
            return _mutex.WaitOne(TimeSpan.FromMilliseconds(50));
        }
        catch (AbandonedMutexException)
        {
            return true; // We own it now.
        }
    }

    // ANSI fields: Latin1, not Encoding.Default (that is UTF-8 in .NET). "°" is 0xB0 in both.
    private static string Ansi(ReadOnlySpan<byte> field) => Encoding.Latin1.GetString(UntilNull(field)).Trim();

    private static string Utf8(ReadOnlySpan<byte> field) => Encoding.UTF8.GetString(UntilNull(field)).Trim();

    private static ReadOnlySpan<byte> UntilNull(ReadOnlySpan<byte> field) =>
        field.IndexOf((byte)0) is var end and >= 0 ? field[..end] : field;

    /// <summary>Logs a problem once (not every poll) and returns null.</summary>
    private List<HwInfoSensor>? Problem(string message)
    {
        if (message != _lastProblem)
            logger.LogWarning("{Message}", message);
        _lastProblem = message;
        return null;
    }

    private void Close()
    {
        _view?.Dispose();
        _file?.Dispose();
        _mutex?.Dispose();
        _view = null;
        _file = null;
        _mutex = null;
    }

    public void Dispose() => Close();
}
