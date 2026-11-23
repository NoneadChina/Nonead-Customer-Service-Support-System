namespace NoneadCSS.Application.DTOs.Responses;

public class PagedResponse<T>
{
    public List<T> Data { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int PageSize { get; set; }
}
