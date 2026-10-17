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
  /** The app in front, which `fps` is for; null when there is no FPS or the app is unknown. */
  game: GameInfo | null;
  /** 1% low FPS; only when frames are counted with ETW. */
  fpsLow: number | null;
}

export interface GameInfo {
  name: string;
  /** PNG at `/api/game-icon/{iconId}`; null when the exe has no readable icon. */
  iconId: string | null;
  /** Graphics API guessed from the loaded DLLs ("DX12", "Vulkan", ...); null when unknown. */
  api: string | null;
}

// `id` is stable and unique; use it as the track key.

export interface DeviceReading {
  id: string;
  name: string;
  tempC: number | null;
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
  source: 'hardware' | 'hwinfo' | 'fake';
  isAdmin: boolean;
  message: string | null;
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline';
