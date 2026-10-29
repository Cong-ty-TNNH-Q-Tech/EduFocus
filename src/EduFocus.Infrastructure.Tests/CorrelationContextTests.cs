using EduFocus.Infrastructure.Logging;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class CorrelationContextTests
{
    [Fact]
    public void BeginScope_sets_current_and_restores_previous_value()
    {
        var context = new CorrelationContext();
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        context.Current.Should().BeNull();

        using (context.BeginScope(outer))
        {
            context.Current.Should().Be(outer);

            using (context.BeginScope(inner))
                context.Current.Should().Be(inner);

            context.Current.Should().Be(outer);
        }

        context.Current.Should().BeNull();
    }

    [Fact]
    public void BeginScope_rejects_empty_guid()
    {
        var context = new CorrelationContext();

        var act = () => context.BeginScope(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
