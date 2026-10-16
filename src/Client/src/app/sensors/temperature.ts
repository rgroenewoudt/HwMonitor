export type TempLevel = 'cool' | 'warm' | 'hot' | 'none';

export interface TempLimits {
  warm: number;
  hot: number;
}

/** Display limits only. CPUs and GPUs may run hotter than drives. */
export const CHIP_LIMITS: TempLimits = { warm: 65, hot: 85 };
export const BOARD_LIMITS: TempLimits = { warm: 60, hot: 80 };
export const DRIVE_LIMITS: TempLimits = { warm: 55, hot: 70 };

export function tempLevel(value: number | null | undefined, limits: TempLimits = CHIP_LIMITS): TempLevel {
  if (value == null) return 'none';
  if (value >= limits.hot) return 'hot';
  if (value >= limits.warm) return 'warm';
  return 'cool';
}
