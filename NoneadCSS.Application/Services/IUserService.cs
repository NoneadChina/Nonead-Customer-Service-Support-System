using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public interface IUserService
{
    /// <summary>
    /// 获取可被分配会话的活跃用户列表（Status=Active 且未软删除）。
    /// 供拥有 conversations.assign 权限的客服主管/客服在分配会话下拉框中使用，
    /// 避免依赖受 SuperAdmin 限制的 GET /api/users 接口。
    /// </summary>
    Task<List<UserResponse>> GetAssignableUsersAsync();
    Task<(List<UserResponse> Items, int Total)> GetPagedUsersAsync(int page, int pageSize, string? keyword = null, int? status = null);
    Task<(int Total, int Active, int Disabled, int Pending)> GetStatsAsync();
    Task<UserResponse?> GetUserByIdAsync(int id);
    Task<UserResponse?> GetUserByEmailAsync(string email);
    Task<UserResponse?> CreateUserAsync(CreateUserRequest request);
    Task<UserResponse?> UpdateUserAsync(int id, UpdateUserRequest request, string? ifMatch = null);
    Task<(bool Success, string? ErrorMessage)> DeleteUserAsync(int id);
    Task<UserResponse?> UpdateProfileAsync(int userId, UpdateProfileRequest request);
    Task<bool> ChangePasswordAsync(int userId, ChangePasswordRequest request);

    // 扩展方法 - 对应 PHP 用户管理
    Task<UserPermissionsResponse?> GetUserPermissionsAsync(int userId);
    Task<UserPermissionsResponse?> UpdateUserPermissionsAsync(int userId, UserPermissionsRequest request);
    Task<UserNotificationSettingsResponse?> GetUserNotificationSettingsAsync(int userId);
    Task<UserNotificationSettingsResponse?> UpdateUserNotificationSettingsAsync(int userId, UserNotificationSettingsRequest request);
    Task UpdateUserLocaleAsync(int userId, string locale);
    Task ActivateUserAsync(int userId);
    Task<bool> UpdateUserStatusAsync(int userId, UserStatus status);
    Task<bool> ResetPasswordAsync(int userId, string newPassword);
    Task<List<UserResponse>> GetUsersByRoleAsync(int role);

    // 垃圾箱
    Task<List<UserResponse>> GetDeletedUsersAsync();
    Task<UserResponse?> RestoreUserAsync(int id);
    Task<bool> HardDeleteUserAsync(int id);
    Task<int> ClearTrashAsync();
}
