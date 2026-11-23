namespace NoneadCSS.Application.DTOs.Responses;

public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public int Code { get; set; } = 200;
    public T? Data { get; set; }
    public string? Message { get; set; }

    public ApiResponse() { }

    public ApiResponse(T data)
    {
        Success = true;
        Code = 200;
        Data = data;
    }

    public ApiResponse(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public ApiResponse(T data, string message)
    {
        Success = true;
        Code = 200;
        Data = data;
        Message = message;
    }

    public static ApiResponse<T> Ok(T data) => new(data);
    public static ApiResponse<T> Ok(T data, string message) => new(data, message);
    public static ApiResponse<T> Error(string message) => new(false, message) { Code = 400 };
    public static ApiResponse<T> Error(int code, string message) => new(false, message) { Code = code };
}

public class ApiResponse : ApiResponse<object>
{
    public ApiResponse() { }

    public ApiResponse(bool success, string message) : base(success, message) { }

    // 注：此处原有一个 `Ok()`（无参）重载，硬编码 message="操作成功"，是全后端唯一调用点
    // （ApiResponseWrapperFilter 的 EmptyResult 分支）已改为按请求文化取词后成为死代码，故删除
    // （2026-09-26 批次 12 切片②）。需要"成功 + 自定义文案"时用 `Ok(string message)` 或直接构造。
    public static ApiResponse Ok(string message) => new(true, message) { Code = 200 };
    public static new ApiResponse Error(string message) => new(false, message) { Code = 400 };
    public static new ApiResponse Error(int code, string message) => new(false, message) { Code = code };
}

