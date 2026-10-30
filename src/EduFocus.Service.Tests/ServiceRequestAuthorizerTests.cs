using EduFocus.Ports.Security;
using EduFocus.Service.Control;
using FluentAssertions;

namespace EduFocus.Service.Tests;

public sealed class ServiceRequestAuthorizerTests
{
    [Fact]
    public void The_service_never_holds_parent_mode_by_default()
    {
        var authorizer = new ServiceRequestAuthorizer();

        authorizer.IsAuthenticated.Should().BeFalse();

        var act = () => authorizer.EnsureAuthorized("disable protection");

        act.Should().Throw<ParentAuthorizationException>();
    }

    [Fact]
    public void An_authorized_scope_grants_access_only_while_it_is_open()
    {
        var authorizer = new ServiceRequestAuthorizer();

        using (authorizer.BeginAuthorizedCommand())
        {
            authorizer.IsAuthenticated.Should().BeTrue();
            authorizer.Invoking(a => a.EnsureAuthorized("disable protection"))
                .Should().NotThrow();
        }

        authorizer.IsAuthenticated.Should().BeFalse();
        authorizer.Invoking(a => a.EnsureAuthorized("disable protection"))
            .Should().Throw<ParentAuthorizationException>();
    }

    [Fact]
    public void Nested_scopes_stay_authorized_until_the_outer_scope_closes()
    {
        var authorizer = new ServiceRequestAuthorizer();

        using (authorizer.BeginAuthorizedCommand())
        {
            var inner = authorizer.BeginAuthorizedCommand();
            inner.Dispose();
            inner.Dispose();

            authorizer.IsAuthenticated.Should().BeTrue();
        }

        authorizer.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task The_scope_flows_into_the_awaited_command_and_closes_after_it()
    {
        var authorizer = new ServiceRequestAuthorizer();
        var observed = false;

        using (authorizer.BeginAuthorizedCommand())
        {
            await Task.Yield();
            observed = authorizer.IsAuthenticated;
        }

        observed.Should().BeTrue();
        authorizer.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Lock_and_record_activity_are_no_ops_for_the_service_scope()
    {
        var authorizer = new ServiceRequestAuthorizer();

        authorizer.Invoking(a =>
            {
                a.RecordActivity();
                a.Lock();
            })
            .Should().NotThrow();

        authorizer.IsAuthenticated.Should().BeFalse();
    }
}
