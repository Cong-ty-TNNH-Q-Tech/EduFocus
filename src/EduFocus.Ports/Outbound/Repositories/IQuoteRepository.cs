using System.Collections.Generic;
using System.Threading.Tasks;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Repositories;

public interface IQuoteRepository
{
    Task<IReadOnlyCollection<Quote>> GetAllAsync();
    Task<IReadOnlyCollection<Quote>> GetByEventTypeAsync(QuoteEventType eventType, string language);
    Task<Quote?> GetByIdAsync(QuoteId id);
    Task AddAsync(Quote quote);
    Task UpdateAsync(Quote quote);
    Task DeleteAsync(QuoteId id);
}
