using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ISystemOptionService
{
    Task<List<SystemOptionResponse>> GetAllAsync();
    Task<List<SystemOptionResponse>> GetByCategoryAsync(string category);
    Task<SystemOptionResponse?> GetByNameAsync(string name);
    Task<string?> GetValueAsync(string name);
    Task<SystemOptionResponse?> UpdateAsync(string name, UpdateSystemOptionRequest request);
    Task<SystemOptionResponse> CreateAsync(CreateSystemOptionRequest request);
    Task<bool> DeleteAsync(string name);
    Task<Dictionary<string, string>> GetAllAsDictionaryAsync();
    Task BatchUpdateAsync(Dictionary<string, string> options);
}
