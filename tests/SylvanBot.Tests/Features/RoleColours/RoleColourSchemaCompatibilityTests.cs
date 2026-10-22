namespace SylvanBot.Tests.Features.RoleColours;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SylvanBot.Infrastructure.Data;

[TestFixture]
public sealed class RoleColourSchemaCompatibilityTests
{
    [Test]
    public void MigrationMatchesCurrentModel()
    {
        // Arrange
        using var db = new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused", postgres => postgres.MigrationsAssembly("SylvanBot")).Options
        );

        var migrations = db.GetService<IMigrationsAssembly>();

        // Act
        var latestMigration =
            migrations.CreateMigration(migrations.Migrations.Last().Value, db.Database.ProviderName!);

        var migrationModel = db.GetService<IModelRuntimeInitializer>().Initialize(latestMigration.TargetModel, true);

        var currentModel = db.GetService<IDesignTimeModel>().Model;

        // Assert
        db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(migrationModel.GetRelationalModel(), currentModel.GetRelationalModel())
            .ShouldBeEmpty();

        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}