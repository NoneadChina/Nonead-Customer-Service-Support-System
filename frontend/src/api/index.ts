// API 层模块化入口
// 所有 API 对象和接口从各业务域模块重新导出，保持向后兼容

export * from './modules/auth';
export * from './modules/core';
export * from './modules/system';
export * from './modules/mail';
export * from './modules/notifications';
export * from './modules/reports';
export * from './modules/automation';
export * from './modules/admin';
export * from './modules/engagement';
export * from './modules/timeTracking';
export * from './modules/customReports';
