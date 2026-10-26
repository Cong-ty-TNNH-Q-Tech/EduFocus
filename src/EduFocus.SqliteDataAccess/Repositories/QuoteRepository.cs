using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Repositories;

namespace EduFocus.SqliteDataAccess.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _context;

    public QuoteRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<Quote>> GetAllAsync()
    {
        return await _context.Quotes.ToListAsync();
    }

    public async Task<IReadOnlyCollection<Quote>> GetByEventTypeAsync(QuoteEventType eventType, string language)
    {
        return await _context.Quotes
            .Where(q => q.EventType == eventType && q.Language == language)
            .ToListAsync();
    }

    public async Task<Quote?> GetByIdAsync(QuoteId id)
    {
        return await _context.Quotes.FindAsync(id);
    }

    public Task AddAsync(Quote quote)
    {
        _context.Quotes.Add(quote);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Quote quote)
    {
        _context.Quotes.Update(quote);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(QuoteId id)
    {
        var quote = await _context.Quotes.FindAsync(id);
        if (quote != null)
        {
            _context.Quotes.Remove(quote);
        }
    }
}
