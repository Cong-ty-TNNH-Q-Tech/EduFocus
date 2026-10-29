using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class ScheduleWindowRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Windows_round_trip_ordered_by_start_time()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteScheduleWindowRepository(db.Options);
        var evening = ScheduleWindow.Create(ScheduleDays.Weekdays, 1140, 1260, "  Giờ học tối  ", Now);
        var morning = ScheduleWindow.Create(
            ScheduleDays.Monday | ScheduleDays.Wednesday,
            480,
            690,
            "Giờ học",
            Now.AddMinutes(1));

        await repository.AddAsync(evening);
        await repository.AddAsync(morning);

        var windows = await repository.ListAsync();

        windows.Should().HaveCount(2);
        windows[0].Id.Should().Be(morning.Id);
        windows[0].Days.Should().Be(ScheduleDays.Monday | ScheduleDays.Wednesday);
        windows[0].StartMinutes.Should().Be(480);
        windows[0].EndMinutes.Should().Be(690);
        windows[0].Label.Should().Be("Giờ học");
        windows[0].IsEnabled.Should().BeTrue();
        windows[0].CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        windows[1].Id.Should().Be(evening.Id);
        windows[1].Label.Should().Be("Giờ học tối");
    }

    [Fact]
    public async Task Get_update_and_delete_round_trip()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteScheduleWindowRepository(db.Options);
        var window = ScheduleWindow.Create(ScheduleDays.Weekdays, 480, 690, "Giờ học", Now);
        await repository.AddAsync(window);

        var loaded = await repository.GetAsync(window.Id);
        loaded.Should().NotBeNull();
        loaded!.StartMinutes.Should().Be(480);

        loaded.Reschedule(ScheduleDays.Weekend, 540, 720, "Cuối tuần", Now.AddMinutes(1));
        loaded.SetEnabled(false, Now.AddMinutes(2));
        await repository.UpdateAsync(loaded);

        var updated = await repository.GetAsync(window.Id);
        updated!.Days.Should().Be(ScheduleDays.Weekend);
        updated.StartMinutes.Should().Be(540);
        updated.EndMinutes.Should().Be(720);
        updated.Label.Should().Be("Cuối tuần");
        updated.IsEnabled.Should().BeFalse();
        updated.UpdatedAtUtc.Should().Be(Now.AddMinutes(2));

        await repository.DeleteAsync(window.Id);

        (await repository.GetAsync(window.Id)).Should().BeNull();
        (await repository.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_unknown_window_is_a_no_op()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteScheduleWindowRepository(db.Options);

        var act = () => repository.DeleteAsync(EduFocus.Domain.ValueObjects.ScheduleWindowId.New());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Window_ending_at_midnight_round_trips()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteScheduleWindowRepository(db.Options);
        var window = ScheduleWindow.Create(ScheduleDays.Sunday, 1380, 1440, null, Now);
        await repository.AddAsync(window);

        var loaded = await repository.GetAsync(window.Id);

        loaded!.EndMinutes.Should().Be(1440);
    }
}
