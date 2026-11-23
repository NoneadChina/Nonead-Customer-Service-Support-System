declare module 'lunar-javascript' {
  export interface Solar {
    getYear(): number;
    getMonth(): number;
    getDay(): number;
    getWeek(): number;
    getLunar(): Lunar;
  }
  export interface Lunar {
    getYear(): number;
    getMonth(): number;
    getDay(): number;
    getMonthInChinese(): string;
    getDayInChinese(): string;
    getYearInGanZhi(): string;
    getYearShengXiao(): string;
    getJieQi(): string;
    getDayYi(): string[];
    getDayJi(): string[];
  }
  export const Solar: {
    fromDate(date: Date): Solar;
    fromYmd(year: number, month: number, day: number): Solar;
  };
  export const Lunar: {
    fromDate(date: Date): Lunar;
    fromYmd(year: number, month: number, day: number): Lunar;
  };
}
