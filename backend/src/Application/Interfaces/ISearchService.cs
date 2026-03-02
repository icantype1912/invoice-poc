using invoice_v1.src.Application.DTOs;

namespace invoice_v1.src.Application.Interfaces
{
    public interface ISearchService
    {
        Task<SearchResultDto> SearchAsync(string query, Guid? vendorId, string userId);
    }
}