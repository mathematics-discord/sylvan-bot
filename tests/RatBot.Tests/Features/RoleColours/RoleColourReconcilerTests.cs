using System.Collections.Immutable;
using Discord.WebSocket;
using Discord;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RatBot.Common;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;
using Serilog;
using Shouldly;

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
    private readonly Dictionary<ulong, IRole> _guildRoles = new Dictionary<ulong, IRole>();
    private readonly Queue<string> _changes = new Queue<string>();

    [SetUp]
    public async Task SetUp()
    {
        _database = new DbContextOptionsBuilder<BotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _client = new DiscordSocketClient();
        _guild = Substitute.For<IGuild>();
        _guild.Id.Returns(1UL);
        _guildRoles.Clear();

        foreach (ulong id in new[] { 1UL, SourceA, SourceB, Uncoloured })
            AddRole(id, Color.Default);

        foreach (ulong id in new[] { Blue, Red, OutsideColour })
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

        await using BotDbContext db = new BotDbContext(_database);

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

        MessageComponent menu = ColourModule.BuildMenu(MemberId, available);

        SelectMenuComponent choices = menu
            .Components.OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<SelectMenuComponent>()
            .Single();

        // Assert
        available.Select(role => role.Id).ShouldBe([Blue, Red], true);
        choices.Options.Select(option => option.Value).ShouldBe(["20", "21"], true);
        ButtonComponent reset = menu.Components.SelectMany(row => ((ActionRowComponent)row).Components)
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
        MessageComponent menu = ColourModule.BuildMenu(MemberId, available);
        ActionRowComponent[] rows = menu.Components.OfType<ActionRowComponent>().ToArray();

        // Assert
        rows.SelectMany(row => row.Components).OfType<SelectMenuComponent>().ShouldBeEmpty();
        ButtonComponent reset = rows.SelectMany(row => row.Components).OfType<ButtonComponent>().Single();
        reset.Label.ShouldBe("Reset");
        reset.Style.ShouldBe(ButtonStyle.Danger);
    }

    [Test]
    public async Task TrySetUserColourAsync_EligibleColour_ReturnsSomeAndUpdatesManagedRoles()
    {
        // Arrange
        _memberRoles.Add(OutsideColour);

        // Act
        ErrorOr<Option<ulong>> result = await EquipAsync(Red);

        // Assert
        result.IsError.ShouldBeFalse();
        result.Value.Value.ShouldBeOfType<Some<ulong>>().Value.ShouldBe(Red);
        _memberRoles.ShouldBe([SourceA, Uncoloured, Red, OutsideColour], true);
        _changes.ShouldBe(["remove:20", "add:21"]);
    }

    [Test]
    public async Task TrySetUserColourAsync_Reset_ReturnsNoneAndPreservesUnmanagedRoles()
    {
        // Arrange
        _memberRoles.Add(OutsideColour);

        // Act
        ErrorOr<Option<ulong>> result = await EquipAsync(null);
        await CheckMemberAsync();

        // Assert
        result.IsError.ShouldBeFalse();
        result.Value.Value.ShouldBeOfType<None>();
        _memberRoles.ShouldBe([SourceA, Uncoloured, OutsideColour], true);
        _changes.ShouldBe(["remove:20"]);
    }

    [Test]
    public async Task UnavailableColour()
    {
        // Arrange
        _memberRoles.Add(OutsideColour);

        // Act
        ErrorOr<Option<ulong>> result = await EquipAsync(OutsideColour);

        // Assert
        result.IsError.ShouldBeTrue();
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
        ErrorOr<Option<ulong>> result = await EquipAsync(Red);

        // Assert
        result.IsError.ShouldBeTrue();
        _changes.ShouldBeEmpty();
        _memberRoles.ShouldBe([SourceA, Uncoloured, Blue], true);
    }

    [Test]
    public async Task BrokenGrant()
    {
        // Arrange: a colour role was deleted after its grant was configured.
        await using BotDbContext db = new BotDbContext(_database);
        db.RoleColourOptions.Add(Grant(SourceB, OutsideColour));
        await db.SaveChangesAsync();
        _guildRoles.Remove(OutsideColour);
        _memberRoles.Add(SourceB);

        // Act
        ErrorOr<Option<ulong>> selection = await EquipAsync(Red);
        ErrorOr<Option<ulong>> removal = await EquipAsync(null);

        // Assert
        selection.IsError.ShouldBeFalse();
        removal.IsError.ShouldBeFalse();
        _memberRoles.ShouldNotContain(Red);
    }

    [Test]
    public async Task EveryoneGrant()
    {
        // Arrange
        _memberRoles.Clear();
        await using BotDbContext db = new BotDbContext(_database);
        db.RoleColourOptions.Add(Grant(1, OutsideColour));
        await db.SaveChangesAsync();

        // Act
        ErrorOr<Option<ulong>> result = await EquipAsync(OutsideColour);
        await CheckMemberAsync();

        // Assert
        result.IsError.ShouldBeFalse();
        _memberRoles.ShouldBe([OutsideColour]);
    }

    [Test]
    public async Task DeletedGrant()
    {
        // Arrange
        await using BotDbContext db = new BotDbContext(_database);

        ImmutableArray<RoleColourOption> oldGrants =
            (await db.RoleColourOptions.AsNoTracking().ToArrayAsync()).ToImmutableArray();

        db.RoleColourOptions.RemoveRange(db.RoleColourOptions);
        await db.SaveChangesAsync();

        // Act
        int incomplete = await _colours.ReconcileGuildAsync(_guild, CancellationToken.None, oldGrants);

        // Assert
        incomplete.ShouldBe(0);
        _memberRoles.ShouldBe([SourceA, Uncoloured], true);
    }

    [Test]
    public async Task ChangesInDifferentGuildsProceedIndependently()
    {
        TaskCompletionSource removalStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        TaskCompletionSource finishRemoval =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _member
            .RemoveRoleAsync(Blue, Arg.Any<RequestOptions>())
            .Returns(async _ =>
            {
                removalStarted.SetResult();
                await finishRemoval.Task;
                _memberRoles.Remove(Blue);
            });

        IGuild otherGuild = Substitute.For<IGuild>();
        otherGuild.Id.Returns(2UL);

        foreach (ulong id in new[] { SourceA, Red })
        {
            IRole role = Substitute.For<IRole>();
            role.Id.Returns(id);
            role.Guild.Returns(otherGuild);
            role.Name.Returns($"Role {id}");
            role.Colors.Returns(new RoleColors(id == SourceA ? Color.Default : Color.Blue));
            otherGuild.GetRole(id).Returns(role);
        }

        HashSet<ulong> otherRoles = [SourceA];
        IGuildUser otherMember = MakeMember(MemberId, otherRoles);
        otherGuild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(otherMember);
        await using BotDbContext db = new BotDbContext(_database);
        db.RoleColourOptions.Add(RoleColourOption.Create(2, SourceA, Red).Value);
        await db.SaveChangesAsync();

        Task<ErrorOr<Option<ulong>>> firstChange = EquipAsync(Red);
        await removalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            ErrorOr<Option<ulong>> otherResult = await _colours
                .TrySetUserColourAsync(otherGuild, MemberId, Red, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            otherResult.IsError.ShouldBeFalse();
            otherRoles.ShouldBe([SourceA, Red], true);
            firstChange.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            finishRemoval.TrySetResult();
        }

        (await firstChange.WaitAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
    }

    [Test]
    public async Task CancellingWaitingUpdatesLeavesTheGuildGateUsable()
    {
        TaskCompletionSource removalStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        TaskCompletionSource finishRemoval =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _member
            .RemoveRoleAsync(Blue, Arg.Any<RequestOptions>())
            .Returns(async _ =>
            {
                removalStarted.SetResult();
                await finishRemoval.Task;
                _memberRoles.Remove(Blue);
            });

        using CancellationTokenSource cancellation = new CancellationTokenSource();

        Task<ErrorOr<Option<ulong>>> firstChange = EquipAsync(Red);
        await removalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);

        Task<ErrorOr<Option<ulong>>> waitingEquip = _colours.TrySetUserColourAsync(_guild, MemberId, Blue, cancellation.Token);

        Task<ErrorOr<Option<ulong>>> waitingCheck = _colours.ReconcileUserRolesAsync(_guild, MemberId, cancellation.Token);
        Task<ErrorOr<Option<ulong>>> nextChange = EquipAsync(Blue);

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

        (await firstChange.WaitAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        (await nextChange.WaitAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        _memberRoles.ShouldBe([SourceA, Uncoloured, Blue], true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MemberDisappearsAfterWrites(bool automaticCheck)
    {
        _guild.GetUserAsync(MemberId, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>())
            .Returns(_member, (IGuildUser)null!);

        if (automaticCheck)
            _memberRoles.Add(Red);

        ErrorOr<Option<ulong>> result = automaticCheck ? await CheckMemberAsync() : await EquipAsync(Red);

        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldContain("Discord roles changed");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovedColourRemainsAfterWrites(bool automaticCheck)
    {
        _member.RemoveRoleAsync(Blue, Arg.Any<RequestOptions>()).Returns(Task.CompletedTask);

        if (automaticCheck)
            _memberRoles.Add(Red);

        ErrorOr<Option<ulong>> result = automaticCheck ? await CheckMemberAsync() : await EquipAsync(Red);

        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldContain("Discord roles changed");
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

        ErrorOr<Option<ulong>> result = await EquipAsync(Red);

        result.IsError.ShouldBe(extraColour);
        if (extraColour)
            result.FirstError.Description.ShouldContain("Discord roles changed");
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

        ErrorOr<Option<ulong>> result = await EquipAsync(null);

        result.IsError.ShouldBeTrue();
        _memberRoles.ShouldContain(OutsideColour);
    }

    [Test]
    public async Task ReconcileUserRolesAsync_FinalSourceLost_ReturnsNoneAndPreservesUnmanagedRoles()
    {
        // Arrange
        _memberRoles.Remove(SourceA);
        _memberRoles.UnionWith([Red, OutsideColour]);

        // Act
        ErrorOr<Option<ulong>> result = await CheckMemberAsync([SourceA, Uncoloured, Blue]);

        // Assert
        result.IsError.ShouldBeFalse();
        result.Value.Value.ShouldBeOfType<None>();
        _memberRoles.ShouldBe([Uncoloured, OutsideColour], true);
    }

    private Task<ErrorOr<Option<ulong>>> EquipAsync(ulong? colour)
    {
        return _colours.TrySetUserColourAsync(_guild, MemberId, colour, CancellationToken.None);
    }

    private Task<ErrorOr<Option<ulong>>> CheckMemberAsync(IReadOnlyCollection<ulong>? before = null)
    {
        return _colours.ReconcileUserRolesAsync(_guild, MemberId, CancellationToken.None, before);
    }

    private static RoleColourOption Grant(ulong source, ulong colour)
    {
        return RoleColourOption.Create(1, source, colour).Value;
    }

    private void AddRole(ulong id, Color colour)
    {
        IRole role = Substitute.For<IRole>();
        role.Id.Returns(id);
        role.Guild.Returns(_guild);

        role.Name.Returns(
            id switch
            {
                Blue => "Blue",
                Red => "Red",
                _ => $"Role {id}"
            }
        );

        role.Colors.Returns(new RoleColors(colour));
        _guildRoles[id] = role;
    }

    private IGuildUser MakeMember(ulong id, HashSet<ulong> roles)
    {
        IGuildUser member = Substitute.For<IGuildUser>();
        member.Id.Returns(id);
        member.RoleIds.Returns(_ => roles.ToArray());

        member
            .RemoveRoleAsync(Arg.Any<ulong>(), Arg.Any<RequestOptions>())
            .Returns(call =>
            {
                ulong role = call.Arg<ulong>();
                roles.Remove(role);
                _changes.Enqueue($"remove:{role}");
                return Task.CompletedTask;
            });

        member
            .AddRoleAsync(Arg.Any<ulong>(), Arg.Any<RequestOptions>())
            .Returns(call =>
            {
                ulong role = call.Arg<ulong>();
                roles.Add(role);
                _changes.Enqueue($"add:{role}");
                return Task.CompletedTask;
            });

        return member;
    }
}