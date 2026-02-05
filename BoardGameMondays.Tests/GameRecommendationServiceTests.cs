using BoardGameMondays.Core;
using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class GameRecommendationServiceTests
{
    [Fact]
    public async Task GetRecommendationAsync_NullUserId_ReturnsNull()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        var result = await service.GetRecommendationAsync(null!, isAdmin: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecommendationAsync_EmptyUserId_ReturnsNull()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        var result = await service.GetRecommendationAsync(string.Empty, isAdmin: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecommendationAsync_NoAgreements_ReturnsNull()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var reviewer = TestData.AddMember(db, "Alice");
        var game = TestData.AddGame(db, "Catan");
        TestData.AddReview(db, game, reviewer);

        // User has no agreements
        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecommendationAsync_UserWithAgreements_ReturnsGameWithReviews()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");

        // Setup: Alice reviews Game1
        var game1 = TestData.AddGame(db, "Catan");
        var review1 = TestData.AddReview(db, game1, alice, rating: 4.5, description: "Great game!");

        // User agrees with Alice's review
        TestData.AddReviewAgreement(db, review1, user.Id, score: 5);

        // Setup: Another game with a review from Alice (but user doesn't agree with this one)
        var game2 = TestData.AddGame(db, "Ticket to Ride");
        var review2 = TestData.AddReview(db, game2, alice, rating: 3.0, description: "Not bad");
        // Only one agreement - on game1's review

        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.NotNull(result);
        Assert.Equal(game2.Id, result!.Id);
        Assert.Equal("Ticket to Ride", result.Name);
    }

    [Fact]
    public async Task GetRecommendationAsync_UserExcludesGamesWithAgreements()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");
        var bob = TestData.AddMember(db, "Bob");

        // Game1: Alice reviews it, user agreeing
        var game1 = TestData.AddGame(db, "Catan");
        var review1 = TestData.AddReview(db, game1, alice);
        TestData.AddReviewAgreement(db, review1, user.Id, score: 4);

        // Game2: Bob reviews it (new reviewer)
        var game2 = TestData.AddGame(db, "Ticket to Ride");
        var review2 = TestData.AddReview(db, game2, bob);

        // Game3: Alice reviews it (user agreeing - so Game3 should be excluded from recommendations)
        var game3 = TestData.AddGame(db, "Splendor");
        var review3 = TestData.AddReview(db, game3, alice);
        TestData.AddReviewAgreement(db, review3, user.Id, score: 3);

        // User should be recommended Game2 (not Game1 or Game3 because they both have agreements)
        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.NotNull(result);
        Assert.Equal(game2.Id, result!.Id);
        // Verify that 2 agreements were added (which excludes game1 and game3)
        Assert.Equal(2, db.ReviewAgreements.Count());
    }

    [Fact]
    public async Task GetRecommendationAsync_AdminExcludesOwnReviews()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var adminUser = TestData.AddUser(db, "admin1", "admin1");
        var admin = TestData.AddMember(db, "Alice");
        
        // Link the admin member to the user via claims
        TestData.LinkMemberToUser(db, admin, adminUser);

        var bob = TestData.AddMember(db, "Bob");

        // Game1: Admin reviews it
        var game1 = TestData.AddGame(db, "Catan");
        TestData.AddReview(db, game1, admin);

        // Game2: Bob reviews it
        var game2 = TestData.AddGame(db, "Ticket to Ride");
        var review2 = TestData.AddReview(db, game2, bob);

        // Admin agrees with Bob (so admin has patterns to base recommendations on)
        TestData.AddReviewAgreement(db, review2, adminUser.Id, score: 4);

        // Admin should NOT be recommended Game1 (their own review)
        var result = await service.GetRecommendationAsync(adminUser.Id, isAdmin: true);

        Assert.NotNull(result);
        Assert.Equal(game2.Id, result!.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_AdminNoMemberLink_StillGetsRecommendations()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var adminUser = TestData.AddUser(db, "admin1", "admin1");
        // Admin is not linked to a member, so they have no reviews to exclude
        
        var bob = TestData.AddMember(db, "Bob");
        var game = TestData.AddGame(db, "Catan");
        var review = TestData.AddReview(db, game, bob);
        TestData.AddReviewAgreement(db, review, adminUser.Id, score: 4);

        // Admin not linked to member, but can still get recommendations based on agreement patterns
        // Since they have no reviews, all games are eligible
        var result = await service.GetRecommendationAsync(adminUser.Id, isAdmin: true);

        Assert.NotNull(result);
        Assert.Equal(game.Id, result!.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_MultipleReviewersScore()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");
        var bob = TestData.AddMember(db, "Bob");
        var charlie = TestData.AddMember(db, "Charlie");

        // User agrees strongly with Alice
        var gA1 = TestData.AddGame(db, "Game A1");
        var revA1 = TestData.AddReview(db, gA1, alice);
        TestData.AddReviewAgreement(db, revA1, user.Id, score: 5);

        // User agrees moderately with Bob
        var gB1 = TestData.AddGame(db, "Game B1");
        var revB1 = TestData.AddReview(db, gB1, bob);
        TestData.AddReviewAgreement(db, revB1, user.Id, score: 3);

        // Game X: Alice and Bob both review it
        // Alice: 5 * 1.5 = 7.5
        // Bob: 3 * 1.5 = 4.5
        // Total: 12.0 / sqrt(2) = 8.49
        var gameX = TestData.AddGame(db, "Game X");
        TestData.AddReview(db, gameX, alice);
        TestData.AddReview(db, gameX, bob);

        // Game Y: Only Charlie reviews (unknown)
        // Charlie: 1.0
        // Total: 1.0 / sqrt(1) = 1.0
        var gameY = TestData.AddGame(db, "Game Y");
        TestData.AddReview(db, gameY, charlie);

        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.NotNull(result);
        Assert.Equal(gameX.Id, result!.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_UnknownReviewerExplanation()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");
        var dave = TestData.AddMember(db, "Dave");

        // User agrees with Alice
        var gA = TestData.AddGame(db, "Game A");
        var revA = TestData.AddReview(db, gA, alice);
        TestData.AddReviewAgreement(db, revA, user.Id, score: 4);

        // Game with unknown reviewer (no prior agreements with Dave)
        var gameUnknown = TestData.AddGame(db, "Game Unknown");
        TestData.AddReview(db, gameUnknown, dave);

        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        // Should recommend Game Unknown because it's the only option (Game A is excluded - user agreed with it)
        Assert.NotNull(result);
        Assert.Equal(gameUnknown.Id, result!.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_AllGamesExcluded_ReturnsNull()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");

        var game1 = TestData.AddGame(db, "Game 1");
        var review1 = TestData.AddReview(db, game1, alice);
        TestData.AddReviewAgreement(db, review1, user.Id, score: 4);

        var game2 = TestData.AddGame(db, "Game 2");
        var review2 = TestData.AddReview(db, game2, alice);
        TestData.AddReviewAgreement(db, review2, user.Id, score: 3);

        // User has agreed with reviews on both games, so both are excluded
        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecommendationAsync_NoGames_ReturnsNull()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");

        var game = TestData.AddGame(db, "Game");
        var review = TestData.AddReview(db, game, alice);
        TestData.AddReviewAgreement(db, review, user.Id, score: 4);

        // All games have agreements, no eligible games exist
        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecommendationAsync_ComplexScenario()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");
        var bob = TestData.AddMember(db, "Bob");
        var charlie = TestData.AddMember(db, "Charlie");

        // User's agreement history:
        // Alice: avg 4.0 (multiple reviews)
        var gTemp1 = TestData.AddGame(db, "Temp1");
        var revTemp1 = TestData.AddReview(db, gTemp1, alice);
        TestData.AddReviewAgreement(db, revTemp1, user.Id, score: 4);

        var gTemp2 = TestData.AddGame(db, "Temp2");
        var revTemp2 = TestData.AddReview(db, gTemp2, alice);
        TestData.AddReviewAgreement(db, revTemp2, user.Id, score: 4);

        // Bob: avg 2.0
        var gTemp3 = TestData.AddGame(db, "Temp3");
        var revTemp3 = TestData.AddReview(db, gTemp3, bob);
        TestData.AddReviewAgreement(db, revTemp3, user.Id, score: 2);

        // Games to recommend from:
        // Option A: Alice review only (score: 4 * 1.5 / sqrt(1) = 6.0)
        var optionA = TestData.AddGame(db, "Option A");
        TestData.AddReview(db, optionA, alice);

        // Option B: Alice + Bob reviews (score: (4*1.5 + 2*1.5) / sqrt(2) = 9 / 1.414 = 6.36)
        var optionB = TestData.AddGame(db, "Option B");
        TestData.AddReview(db, optionB, alice);
        TestData.AddReview(db, optionB, bob);

        // Option C: Charlie + Alice reviews (score: (4*1.5 + 1) / sqrt(2) = 7 / 1.414 = 4.95)
        var optionC = TestData.AddGame(db, "Option C");
        TestData.AddReview(db, optionC, charlie);
        TestData.AddReview(db, optionC, alice);

        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.NotNull(result);
        // Option B should have the highest score (6.36)
        Assert.Equal(optionB.Id, result!.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_LoadsCorrectGameData()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");

        var gTemp = TestData.AddGame(db, "Temp");
        var revTemp = TestData.AddReview(db, gTemp, alice);
        TestData.AddReviewAgreement(db, revTemp, user.Id, score: 4);

        var recommendedGame = TestData.AddGame(db, "Recommended", status: (int)GameStatus.Playing);
        recommendedGame.Tagline = "A great game";
        recommendedGame.MinPlayers = 2;
        recommendedGame.MaxPlayers = 4;
        db.SaveChanges();
        var revRec = TestData.AddReview(db, recommendedGame, alice);

        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.NotNull(result);
        Assert.Equal("Recommended", result!.Name);
        Assert.Equal(GameStatus.Playing, result.Status);
        Assert.Equal("A great game", result.Tagline);
        Assert.Equal(2, result.MinPlayers);
        Assert.Equal(4, result.MaxPlayers);
    }

    [Fact]
    public async Task GetRecommendationAsync_MultipleAgreementsWithSameReviewer()
    {
        using var factory = new TestDbFactory();
        var service = new GameRecommendationService(factory);

        await using var db = factory.CreateDbContext();
        var user = TestData.AddUser(db, "user1", "user1");
        var alice = TestData.AddMember(db, "Alice");

        // User has multiple agreements with Alice (different reviews)
        var g1 = TestData.AddGame(db, "Game 1");
        var rev1 = TestData.AddReview(db, g1, alice);
        TestData.AddReviewAgreement(db, rev1, user.Id, score: 5);

        var g2 = TestData.AddGame(db, "Game 2");
        var rev2 = TestData.AddReview(db, g2, alice);
        TestData.AddReviewAgreement(db, rev2, user.Id, score: 3);

        // Game X: Alice reviews (avg agreement should be (5+3)/2 = 4)
        // Score: 4 * 1.5 / sqrt(1) = 6.0
        var gameX = TestData.AddGame(db, "Game X");
        TestData.AddReview(db, gameX, alice);

        var result = await service.GetRecommendationAsync(user.Id, isAdmin: false);

        Assert.NotNull(result);
        Assert.Equal(gameX.Id, result!.Id);
    }
}
