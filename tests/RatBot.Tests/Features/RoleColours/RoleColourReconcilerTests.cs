using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Discord.WebSocket;
using Discord;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;
using Serilog;

namespace RatBot.Tests.Features.RoleColours;

[TestFixture]
public sealed class RoleColourReconcilerTests
{
    private const ulong SourceA = 10;
    private const ulong SourceB = 11;
    private const ulong Uncoloured = 12;
    private const ulong Blue = 20;
    private const ulong Red = 21;
    private const ulong OutsideColour = 22;
    private const ulong MemberId = 100;

    private DbContextOptions<BotDbContext> _database = null!;
    private DiscordSocketClient _client = null!;
    private IGuild _guild = null!;
    private IGuildUser _member = null!;
    private RoleColourReconciler _colours = null!;
    private readonly HashSet<ulong> _memberRoles = [];
    private readonly Dictionary<ulong, IRole> _guildRoles = new();
    private readonly Queue<string> _changes = new();

    [SetUp]
    public async Task SetUp()
    {
        _database = new DbContextOptionsBuilder<BotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _client = new DiscordSocketClient();
        _guild = Substitute.For<IGuild>();
        _guild.Id.Returns(1UL);
        _guildRoles.Clear();

        foreach (var id in new[] { 1UL, SourceA, SourceB, Uncoloured })
            AddRole(id, Color.Default);

        foreach (var id in new[] { Blue, Red, OutsideColour })
            AddRole(id, Color.Blue);

        _guild.GetRole(Arg.Any<ulong>()).Returns(call => _guildRoles.GetValueOrDefault(call.Arg<ulong>())!);

        // This member starts with access to blue and red, and is wearing blue.
        _memberRoles.Clear();
        _memberRoles.UnionWith([SourceA, Uncoloured, Blue]);
        _changes.Clear();
        _member = MakeMember(MemberId, _memberRoles);
        _guild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(_member);
        _guild.GetUsersAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns([_member]);

        IDbContextFactory<BotDbContext> factory = Substitute.For<IDbContextFactory<BotDbContext>>();

        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new BotDbContext(_database)));

        _colours = new RoleColourReconciler(factory, Log.Logger, _client);

        await using var db = new BotDbContext(_database);

        db.RoleColourOptions.AddRange(Grant(SourceA, Blue), Grant(SourceA, Red), Grant(SourceB, Blue));

        await db.SaveChangesAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _client.DisposeAsync();
    }

    [Test]
    public async Task AvailableColours()
    {
        // Arrange
        _memberRoles.Add(SourceB);

        // Act
        ImmutableArray<IRole> available =
            await _colours.ListEligibleColoursAsync(_guild, MemberId, CancellationToken.None);

        var menu = ColourModule.BuildMenu(MemberId, available);

        var choices = menu
            .Components.OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<SelectMenuComponent>()
            .Single();

        // Assert
        available.Select(role => role.Id).ShouldBe([Blue, Red], true);
        choices.Options.Select(option => option.Value).ShouldBe(["20", "21"], true);
        var reset = menu.Components.SelectMany(row => ((ActionRowComponent)row).Components)
            .OfType<ButtonComponent>().Single();
        reset.Label.ShouldBe("Reset");
        reset.CustomId.ShouldBe($"colour-swap:reset:{MemberId}");
        reset.Style.ShouldBe(ButtonStyle.Danger);
    }

    [Test]
    public void ResetWithoutAvailableColours()
    {
        // Arrange
        ImmutableArray<IRole> available = [];

        // Act
        var menu = ColourModule.BuildMenu(MemberId, available);
        ActionRowComponent[] rows = menu.Components.OfType<ActionRowComponent>().ToArray();

        // Assert
        rows.SelectMany(row => row.Components).OfType<SelectMenuComponent>().ShouldBeEmpty();
        var reset = rows.SelectMany(row => row.Components).OfType<ButtonComponent>().Single();
        reset.Label.ShouldBe("Reset");
        reset.Style.ShouldBe(ButtonStyle.Danger);
    }

    [Test]
    public async Task TrySetUserColourAsync_EligibleColour_ReturnsSomeAndUpdatesManagedRoles()
    {
        // Arrange
        _memberRoles.Add(OutsideColour);

        // Act
        Option<ulong> result = await EquipAsync(Red);

        // Assert
        Option<ulong> colour = result;
        colour.Value.ShouldBeOfType<Some<ulong>>().Value.ShouldBe(Red);
        _memberRoles.ShouldBe([SourceA, Uncoloured, Red, OutsideColour], true);
        _changes.ShouldBe(["remove:20", "add:21"]);
    }

    [Test]
    public async Task TrySetUserColourAsync_Reset_ReturnsNoneAndPreservesUnmanagedRoles()
    {
        // Arrange
        _memberRoles.Add(OutsideColour);

        // Act
        Option<ulong> result = await EquipAsync(null);
        await CheckMemberAsync();

        // Assert
        result.Value.ShouldBeOfType<None>();
        _memberRoles.ShouldBe([SourceA, Uncoloured, OutsideColour], true);
        _changes.ShouldBe(["remove:20"]);
    }

    [Test]
    public async Task UnavailableColour()
    {
        // Arrange
        _memberRoles.Add(OutsideColour);

        // Act
        var exception = await Should.ThrowAsync<ArgumentException>(() => EquipAsync(OutsideColour));

        // Assert
        exception.ParamName.ShouldBe("colourId");
        _changes.ShouldBeEmpty();
        _memberRoles.ShouldBe([SourceA, Uncoloured, Blue, OutsideColour], true);
    }

    [Test]
    public async Task LosingSources()
    {
        // Arrange: both sources grant blue.
        _memberRoles.Add(SourceB);
        _memberRoles.Remove(SourceA);

        // Act: lose one source.
        await CheckMemberAsync([SourceA, SourceB, Uncoloured, Blue]);

        // Assert: the other source still grants blue.
        _memberRoles.ShouldContain(Blue);
        _changes.ShouldBeEmpty();

        // Arrange
        _memberRoles.Remove(SourceB);

        // Act: lose the final source.
        await CheckMemberAsync([SourceB, Uncoloured, Blue]);

        // Assert
        _memberRoles.ShouldBe([Uncoloured]);
        _changes.ShouldBe(["remove:20"]);

        // Arrange
        _memberRoles.Add(SourceA);
        _changes.Clear();

        // Act: regain access.
        await CheckMemberAsync([Uncoloured]);

        // Assert: blue is not restored and no default is equipped.
        _memberRoles.ShouldBe([SourceA, Uncoloured], true);
        _changes.ShouldBeEmpty();
    }

    [Test]
    public async Task GainingAnotherSource()
    {
        // Arrange
        _memberRoles.Add(SourceB);

        // Act
        await CheckMemberAsync([SourceA, Uncoloured, Blue]);

        // Assert
        _memberRoles.ShouldBe([SourceA, SourceB, Uncoloured, Blue], true);
        _changes.ShouldBeEmpty();
    }

    [Test]
    public async Task WearingSeveralColours()
    {
        // Arrange
        _memberRoles.UnionWith([Red, OutsideColour]);

        // Act
        await CheckMemberAsync();

        // Assert
        _memberRoles.ShouldBe([SourceA, Uncoloured, OutsideColour], true);
        _changes.ShouldBe(["remove:20", "remove:21"], true);
    }

    [Test]
    public async Task NoSourceRoles()
    {
        // Arrange
        _memberRoles.Remove(SourceA);
        _memberRoles.Add(OutsideColour);

        // Act
        await CheckMemberAsync();

        // Assert
        _memberRoles.ShouldBe([Uncoloured, Blue, OutsideColour], true);
        _changes.ShouldBeEmpty();
    }

    [Test]
    public async Task RemovalFails()
    {
        // Arrange
        _member.RemoveRoleAsync(Blue, Arg.Any<RequestOptions>())
            .Returns(_ => Task.FromException(new TimeoutException()));

        // Act
        await Should.ThrowAsync<TimeoutException>(() => EquipAsync(Red));

        // Assert
        _changes.ShouldBeEmpty();
        _memberRoles.ShouldBe([SourceA, Uncoloured, Blue], true);
    }

    [Test]
    public async Task BrokenGrant()
    {
        // Arrange: a colour role was deleted after its grant was configured.
        await using var db = new BotDbContext(_database);
        db.RoleColourOptions.Add(Grant(SourceB, OutsideColour));
        await db.SaveChangesAsync();
        _guildRoles.Remove(OutsideColour);
        _memberRoles.Add(SourceB);

        // Act
        Option<ulong> selection = await EquipAsync(Red);
        Option<ulong> removal = await EquipAsync(null);

        // Assert
        _memberRoles.ShouldNotContain(Red);
    }

    [Test]
    public async Task EveryoneGrant()
    {
        // Arrange
        _memberRoles.Clear();
        await using var db = new BotDbContext(_database);
        db.RoleColourOptions.Add(Grant(1, OutsideColour));
        await db.SaveChangesAsync();

        // Act
        Option<ulong> result = await EquipAsync(OutsideColour);
        await CheckMemberAsync();

        // Assert
        _memberRoles.ShouldBe([OutsideColour]);
    }

    [Test]
    public async Task DeletedGrant()
    {
        // Arrange
        await using var db = new BotDbContext(_database);

        ImmutableArray<RoleColourOption> oldGrants =
            (await db.RoleColourOptions.AsNoTracking().ToArrayAsync()).ToImmutableArray();

        db.RoleColourOptions.RemoveRange(db.RoleColourOptions);
        await db.SaveChangesAsync();

        // Act
        var incomplete = await _colours.ReconcileGuildAsync(_guild, CancellationToken.None, oldGrants);

        // Assert
        incomplete.ShouldBe(0);
        _memberRoles.ShouldBe([SourceA, Uncoloured], true);
    }

    [Test]
    public async Task ChangesInDifferentGuildsProceedIndependently()
    {
        var removalStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var finishRemoval =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _member
            .RemoveRoleAsync(Blue, Arg.Any<RequestOptions>())
            .Returns(async _ =>
            {
                removalStarted.SetResult();
                await finishRemoval.Task;
                _memberRoles.Remove(Blue);
            });

        var otherGuild = Substitute.For<IGuild>();
        otherGuild.Id.Returns(2UL);

        foreach (var id in new[] { SourceA, Red })
        {
            var role = Substitute.For<IRole>();
            role.Id.Returns(id);
            role.Guild.Returns(otherGuild);
            role.Name.Returns($"Role {id}");
            role.Colors.Returns(new RoleColors(id == SourceA ? Color.Default : Color.Blue));
            otherGuild.GetRole(id).Returns(role);
        }

        HashSet<ulong> otherRoles = [SourceA];
        var otherMember = MakeMember(MemberId, otherRoles);
        otherGuild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(otherMember);
        await using var db = new BotDbContext(_database);
        db.RoleColourOptions.Add(RoleColourOption.Create(2, SourceA, Red));
        await db.SaveChangesAsync();

        Task<Option<ulong>> firstChange = EquipAsync(Red);
        await removalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Option<ulong> otherResult = await _colours
                .TrySetUserColourAsync(otherGuild, MemberId, Red, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            otherRoles.ShouldBe([SourceA, Red], true);
            firstChange.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            finishRemoval.TrySetResult();
        }

        await firstChange.WaitAsync(TimeSpan.FromSeconds(5));
    }

#pragma warning disable MA0040
    [Test]
    [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
    public async Task CancellingWaitingUpdatesLeavesTheGuildGateUsable()
    {
        var removalStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var finishRemoval =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _member
            .RemoveRoleAsync(Blue, Arg.Any<RequestOptions>())
            .Returns(async _ =>
            {
                removalStarted.SetResult();
                await finishRemoval.Task;
                _memberRoles.Remove(Blue);
            });

        using var cancellation = new CancellationTokenSource();

        Task<Option<ulong>> firstChange = EquipAsync(Red);
        await removalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);

        Task<Option<ulong>> waitingEquip = _colours.TrySetUserColourAsync(
            _guild,
            MemberId,
            Blue,
            cancellation.Token);

        Task<Option<ulong>> waitingCheck = _colours.ReconcileUserRolesAsync(
            _guild,
            MemberId,
            cancellation.Token);
        Task<Option<ulong>> nextChange = EquipAsync(Blue);

        try
        {
            await cancellation.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() =>
                waitingEquip.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token));

            await Should.ThrowAsync<OperationCanceledException>(() =>
                waitingCheck.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token));

            nextChange.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            finishRemoval.TrySetResult();
        }

        await firstChange.WaitAsync(TimeSpan.FromSeconds(5));
        await nextChange.WaitAsync(TimeSpan.FromSeconds(5));
        _memberRoles.ShouldBe([SourceA, Uncoloured, Blue], ignoreOrder: true);
    }
#pragma warning restore MA0040

    [TestCase(false)]
    [TestCase(true)]
    public async Task MemberDisappearsAfterWrites(bool automaticCheck)
    {
        _guild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>())
            .Returns(_member, (IGuildUser)null!);

        if (automaticCheck)
            _memberRoles.Add(Red);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            automaticCheck ? CheckMemberAsync() : EquipAsync(Red));

        exception.Message.ShouldContain("Discord roles changed");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovedColourRemainsAfterWrites(bool automaticCheck)
    {
        _member.RemoveRoleAsync(Blue, Arg.Any<RequestOptions>()).Returns(Task.CompletedTask);

        if (automaticCheck)
            _memberRoles.Add(Red);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            automaticCheck ? CheckMemberAsync() : EquipAsync(Red));

        exception.Message.ShouldContain("Discord roles changed");
        _memberRoles.ShouldContain(Blue);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EquipmentOnlyChecksManagedColours(bool extraColour)
    {
        _member
            .AddRoleAsync(Red, Arg.Any<RequestOptions>())
            .Returns(_ =>
            {
                _memberRoles.Add(OutsideColour);
                _memberRoles.Add(Red);

                if (extraColour)
                    _memberRoles.Add(Blue);

                return Task.CompletedTask;
            });

        if (extraColour)
        {
            var exception = await Should.ThrowAsync<InvalidDataException>(() => EquipAsync(Red));
            exception.Message.ShouldContain("Discord roles changed");
        }
        else
        {
            await EquipAsync(Red);
        }

        _memberRoles.ShouldContain(OutsideColour);
    }

    [Test]
    public async Task RemovingColourRejectsAnUnexpectedRemainingColour()
    {
        _member
            .RemoveRoleAsync(Blue, Arg.Any<RequestOptions>())
            .Returns(_ =>
            {
                _memberRoles.Remove(Blue);
                _memberRoles.Add(OutsideColour);
                _memberRoles.Add(Red);
                return Task.CompletedTask;
            });

        var exception = await Should.ThrowAsync<InvalidDataException>(() => EquipAsync(null));

        exception.Message.ShouldContain("Discord roles changed");
        _memberRoles.ShouldContain(OutsideColour);
    }

    [Test]
    public async Task ReconcileUserRolesAsync_FinalSourceLost_ReturnsNoneAndPreservesUnmanagedRoles()
    {
        // Arrange
        _memberRoles.Remove(SourceA);
        _memberRoles.UnionWith([Red, OutsideColour]);

        // Act
        Option<ulong> result = await CheckMemberAsync([SourceA, Uncoloured, Blue]);

        // Assert
        result.Value.ShouldBeOfType<None>();
        _memberRoles.ShouldBe([Uncoloured, OutsideColour], true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReconcileGuildAsync_MemberFails_CountsFailureAndContinues(bool missingMember)
    {
        // Arrange
        _memberRoles.Add(Red);
        HashSet<ulong> laterRoles = [SourceA, Blue, Red];
        var laterMember = MakeMember(MemberId + 1, laterRoles);
        _guild.GetUsersAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns([_member, laterMember]);
        _guild.GetUserAsync(MemberId + 1, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(laterMember);

        if (missingMember)
        {
            _guild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>())
                .Returns((IGuildUser)null!);
        }
        else
        {
            _member.RemoveRoleAsync(Arg.Any<ulong>(), Arg.Any<RequestOptions>())
                .Returns(Task.FromException(new TimeoutException("Role removal failed")));
        }

        // Act
        var incomplete = await _colours.ReconcileGuildAsync(_guild, CancellationToken.None);

        // Assert
        incomplete.ShouldBe(1);
        _memberRoles.ShouldContain(Blue);
        laterRoles.ShouldBe([SourceA]);
    }

    [Test]
    public async Task TrySetUserColourAsync_MissingMember_ThrowsBeforeChangingRoles()
    {
        // Arrange
        _guild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns((IGuildUser)null!);

        // Act
        await Should.ThrowAsync<KeyNotFoundException>(() => EquipAsync(Red));

        // Assert
        _changes.ShouldBeEmpty();
    }

    private Task<Option<ulong>> EquipAsync(ulong? colour)
    {
        return _colours.TrySetUserColourAsync(_guild, MemberId, colour, CancellationToken.None);
    }

    private Task<Option<ulong>> CheckMemberAsync(IReadOnlyCollection<ulong>? before = null)
    {
        return _colours.ReconcileUserRolesAsync(_guild, MemberId, CancellationToken.None, before);
    }

    private static RoleColourOption Grant(ulong source, ulong colour)
    {
        return RoleColourOption.Create(1, source, colour);
    }

    private void AddRole(ulong id, Color colour)
    {
        var role = Substitute.For<IRole>();
        role.Id.Returns(id);
        role.Guild.Returns(_guild);

        role.Name.Returns(
            id switch
            {
                Blue => "Blue",
                Red => "Red",
                _ => $"Role {id}",
            }
        );

        role.Colors.Returns(new RoleColors(colour));
        _guildRoles[id] = role;
    }

    private IGuildUser MakeMember(ulong id, HashSet<ulong> roles)
    {
        var member = Substitute.For<IGuildUser>();
        member.Id.Returns(id);
        member.RoleIds.Returns(_ => roles.ToArray());

        member
            .RemoveRoleAsync(Arg.Any<ulong>(), Arg.Any<RequestOptions>())
            .Returns(call =>
            {
                var role = call.Arg<ulong>();
                roles.Remove(role);
                _changes.Enqueue($"remove:{role}");
                return Task.CompletedTask;
            });

        member
            .AddRoleAsync(Arg.Any<ulong>(), Arg.Any<RequestOptions>())
            .Returns(call =>
            {
                var role = call.Arg<ulong>();
                roles.Add(role);
                _changes.Enqueue($"add:{role}");
                return Task.CompletedTask;
            });

        return member;
    }
}