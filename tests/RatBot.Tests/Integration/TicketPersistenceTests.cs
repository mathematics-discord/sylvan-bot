using Microsoft.EntityFrameworkCore;
using Npgsql;
using RatBot.Features.Tickets;

namespace RatBot.Tests.Integration;

[TestFixture]
public sealed class TicketPersistenceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
    }

    [Test]
    public async Task SaveChangesAsync_SimultaneousNotesAcrossGuilds_AllocatesDistinctPositiveNumbers()
    {
        // Arrange
        var writes = Enumerable.Range(0, 12)
            .Select(index => SaveNoteAsync(guildId: (ulong)(index % 2 + 1)))
            .ToArray();

        // Act
        var notes = await Task.WhenAll(writes);

        // Assert
        var numbers = notes
            .Select(note => note.TicketNumber)
            .ToArray();

        numbers.ShouldBeUnique();
        numbers.ShouldAllBe(number => number > 0);
        await using var dbContext = PostgresDatabaseFixture.CreateDbContext();
        var guildNotes = await dbContext.TicketNotes
            .AsNoTracking()
            .Where(note => note.GuildId == 1)
            .ToArrayAsync();

        guildNotes.Length.ShouldBe(6);
        guildNotes.ShouldAllBe(note => note.GuildId == 1 && note.PublishedMessageId == null);
    }

    [Test]
    public async Task SaveChangesAsync_LegacyNumbers_AreUniqueWithinGuildWhileNullsRemainAllowed()
    {
        // Arrange
        await SaveNoteAsync(guildId: 1, legacyTicketNumber: 42);
        await SaveNoteAsync(guildId: 2, legacyTicketNumber: 42);
        await SaveNoteAsync(guildId: 1);
        await SaveNoteAsync(guildId: 1);

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() => SaveNoteAsync(guildId: 1, legacyTicketNumber: 42));

        // Assert
        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        await using var dbContext = PostgresDatabaseFixture.CreateDbContext();
        var notes = await dbContext.TicketNotes
            .AsNoTracking()
            .Where(note => note.GuildId == 1)
            .ToArrayAsync();

        notes.Length.ShouldBe(3);
    }

    private static async Task<TicketNote> SaveNoteAsync(ulong guildId, long? legacyTicketNumber = null)
    {
        await using var dbContext = PostgresDatabaseFixture.CreateDbContext();
        var note = new TicketNote
        {
            GuildId = guildId,
            SubjectUserId = 123,
            AuthorUserId = 456,
            Text = "Preserved note",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LegacyTicketNumber = legacyTicketNumber,
        };

        dbContext.TicketNotes.Add(note);
        await dbContext.SaveChangesAsync();
        return note;
    }
}
