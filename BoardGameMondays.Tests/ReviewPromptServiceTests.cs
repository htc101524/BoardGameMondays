using BoardGameMondays.Core;
using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class ReviewPromptServiceTests
{
    [Fact]
    public async Task GetUnreviewedGamesAsync_WithNoGamesPlayed_ReturnsEmpty()
    {
        // Arrange
        using var factory = new TestDbFactory();
        await using var db = factory.CreateDbContext();
        
        var memberId = Guid.NewGuid();
        var emailSender = new NoOpEmailSender();
        var service = new ReviewPromptService(factory, emailSender);

        // Act
        var result = await service.GetUnreviewedGamesAsync(memberId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUnreviewedGamesAsync_WithAllGamesReviewed_ReturnsEmpty()
    {
        // Arrange
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            var member = TestData.AddMember(db, "TestMember");
            member.Email = "test@example.com";
            
            var game = TestData.AddGame(db, "Test Game");
            var gameNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            var gameNightGame = TestData.AddGameNightGame(db, gameNight, game, isPlayed: true);
            
            // Add player
            var player = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            db.GameNightGamePlayers.Add(player);

            // Add a review so the game is reviewed
            var review = TestData.AddReview(db, game, member, rating: 5.0, description: "Great game!");
            db.SaveChanges();
        }

        var emailSender = new NoOpEmailSender();
        await using var db2 = factory.CreateDbContext();
        var service = new ReviewPromptService(factory, emailSender);

        // Act
        var memberId = db2.Members.First().Id;
        var result = await service.GetUnreviewedGamesAsync(memberId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUnreviewedGamesAsync_WithUnreviewedGames_ReturnsGames()
    {
        // Arrange
        using var factory = new TestDbFactory();
        Guid memberId;
        Guid game1Id;
        Guid game2Id;

        await using (var db = factory.CreateDbContext())
        {
            var member = TestData.AddMember(db, "TestMember");
            member.Email = "test@example.com";
            memberId = member.Id;
            
            var game1 = TestData.AddGame(db, "Test Game 1");
            var game2 = TestData.AddGame(db, "Test Game 2");
            game1Id = game1.Id;
            game2Id = game2.Id;
            
            var gameNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            var gameNightGame1 = TestData.AddGameNightGame(db, gameNight, game1, isPlayed: true);
            var gameNightGame2 = TestData.AddGameNightGame(db, gameNight, game2, isPlayed: true);
            
            // Add players
            var player1 = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame1.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            var player2 = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame2.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            db.GameNightGamePlayers.Add(player1);
            db.GameNightGamePlayers.Add(player2);
            db.SaveChanges();
        }

        var emailSender = new NoOpEmailSender();
        var service = new ReviewPromptService(factory, emailSender);

        // Act
        var result = await service.GetUnreviewedGamesAsync(memberId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.True(result.Any(g => g.GameId == game1Id));
        Assert.True(result.Any(g => g.GameId == game2Id));
    }

    [Fact]
    public async Task GetUnreviewedGamesAsync_WithFilteredGameIds_ReturnsOnlyRequestedGames()
    {
        // Arrange
        using var factory = new TestDbFactory();
        Guid memberId;
        Guid game1Id;
        Guid game2Id;

        await using (var db = factory.CreateDbContext())
        {
            var member = TestData.AddMember(db, "TestMember");
            member.Email = "test@example.com";
            memberId = member.Id;
            
            var game1 = TestData.AddGame(db, "Test Game 1");
            var game2 = TestData.AddGame(db, "Test Game 2");
            game1Id = game1.Id;
            game2Id = game2.Id;
            
            var gameNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            var gameNightGame1 = TestData.AddGameNightGame(db, gameNight, game1, isPlayed: true);
            var gameNightGame2 = TestData.AddGameNightGame(db, gameNight, game2, isPlayed: true);
            
            // Add players
            var player1 = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame1.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            var player2 = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame2.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            db.GameNightGamePlayers.Add(player1);
            db.GameNightGamePlayers.Add(player2);
            db.SaveChanges();
        }

        var emailSender = new NoOpEmailSender();
        var service = new ReviewPromptService(factory, emailSender);

        // Act - only request game1
        var result = await service.GetUnreviewedGamesAsync(memberId, new[] { game1Id });

        // Assert
        Assert.Single(result);
        Assert.Equal(game1Id, result[0].GameId);
    }

    [Fact]
    public async Task SendReviewPromptsAsync_WithNoPlayedGames_ReturnZero()
    {
        // Arrange
        using var factory = new TestDbFactory();
        Guid gameNightId;

        await using (var db = factory.CreateDbContext())
        {
            var gameNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            gameNightId = gameNight.Id;
        }

        var emailSender = new NoOpEmailSender();
        var service = new ReviewPromptService(factory, emailSender);

        // Act
        var result = await service.SendReviewPromptsAsync(gameNightId, delayHours: 0);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task SendReviewPromptsAsync_WithUnreviewedGames_CreatesPromptRecords()
    {
        // Arrange
        using var factory = new TestDbFactory();
        Guid gameNightId;
        Guid memberId;
        Guid gameId;

        await using (var db = factory.CreateDbContext())
        {
            var member = TestData.AddMember(db, "TestMember");
            member.Email = "test@example.com";
            memberId = member.Id;
            
            var game = TestData.AddGame(db, "Test Game");
            gameId = game.Id;
            
            var gameNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            gameNightId = gameNight.Id;
            
            var gameNightGame = TestData.AddGameNightGame(db, gameNight, game, isPlayed: true);
            
            var player = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            db.GameNightGamePlayers.Add(player);
            db.SaveChanges();
        }

        var emailSender = new NoOpEmailSender();
        var service = new ReviewPromptService(factory, emailSender);

        // Act
        var result = await service.SendReviewPromptsAsync(gameNightId, delayHours: 0);

        // Assert
        Assert.Equal(1, result);

        // Verify prompt record was created
        await using var verifyDb = factory.CreateDbContext();
        var promptRecord = await verifyDb.ReviewPromptSents
            .FirstOrDefaultAsync(r => r.MemberId == memberId && r.GameId == gameId);
        Assert.NotNull(promptRecord);
    }

    [Fact]
    public async Task SendReviewPromptsAsync_PreventsDoubleSendingForSameGame()
    {
        // Arrange
        using var factory = new TestDbFactory();
        Guid gameNightId;

        await using (var db = factory.CreateDbContext())
        {
            var member = TestData.AddMember(db, "TestMember");
            member.Email = "test@example.com";
            
            var game = TestData.AddGame(db, "Test Game");
            var gameNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            gameNightId = gameNight.Id;
            
            var gameNightGame = TestData.AddGameNightGame(db, gameNight, game, isPlayed: true);
            
            var player = new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            };
            db.GameNightGamePlayers.Add(player);
            db.SaveChanges();
        }

        var emailSender = new NoOpEmailSender();
        var service = new ReviewPromptService(factory, emailSender);

        // Act - Send the first email
        var firstResult = await service.SendReviewPromptsAsync(gameNightId, delayHours: 0);
        Assert.Equal(1, firstResult);

        // Try sending again - should return 0 since already prompted
        var secondResult = await service.SendReviewPromptsAsync(gameNightId, delayHours: 0);
        Assert.Equal(0, secondResult);
    }
}

/// <summary>
/// Mock email sender that doesn't actually send emails (for testing).
/// </summary>
internal sealed class NoOpEmailSender : IEmailSender
{
    public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        return Task.CompletedTask;
    }
}
