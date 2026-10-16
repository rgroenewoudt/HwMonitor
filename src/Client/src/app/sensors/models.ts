// Same shape as HwMonitor.Models.SensorSnapshot on the server.

export interface SensorSnapshot {
  timestamp: string;
  cpu: DeviceReading | null;
  gpus: DeviceReading[];
  systemTemps: TempReading[];
  fans: FanReading[];
  memory: MemoryReading | null;
  fps: number | null;
  status: SourceStatus;
}

// `id` is stable and unique; use it as the track key.

export interface DeviceReading {
  id: string;
  name: string;
  tempC: number | null;
  hotSpotC: number | null;
  loadPercent: number | null;
  powerW: number | null;
  /** GPU memory (VRAM); null for the CPU. */
  memory: MemoryReading | null;
}

export interface MemoryReading {
  usedGb: number;
  totalGb: number;
}

export interface TempReading {
  id: string;
  name: string;
  group: string;
  tempC: number;
}

export interface FanReading {
  id: string;
  name: string;
  source: string;
  rpm: number;
}

export interface SourceStatus {
  source: 'hardware' | 'fake';
  isAdmin: boolean;
  driverInstalled: boolean;
  message: string | null;
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline';
