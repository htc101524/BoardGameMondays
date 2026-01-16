using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Data;

public static class DataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        // Ensure singleton featured-state row exists.
        if (!await db.FeaturedState.AnyAsync(x => x.Id == 1, ct))
        {
            db.FeaturedState.Add(new FeaturedStateEntity { Id = 1, FeaturedGameId = null });
            await db.SaveChangesAsync(ct);
        }

        // Seed members and games if empty.
        if (await db.Games.AnyAsync(ct))
        {
            return;
        }

        var henry = await GetOrCreateMemberAsync(db, "Henry", "Organizes the Monday game nights.", ct);
        var alex = await GetOrCreateMemberAsync(db, "Alex", "Loves puzzly euros and clever drafting.", ct);
        var sam = await GetOrCreateMemberAsync(db, "Sam", "Always up for a fast teach and a rematch.", ct);

        var cascadia = new BoardGameEntity
        {
            Id = Guid.NewGuid(),
            Name = "Cascadia",
            Status = 0,
            Tagline = "A calm, clever tile-laying puzzle with satisfying combos.",
            ImageUrl = "images/placeholder-game-cover.svg"
        };

        var reviewedOn = new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);

        cascadia.Reviews.AddRange(
        [
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                ReviewerId = henry.Id,
                Rating = 9.0,
                Description = "Soothing, quick to teach, and the scoring goals keep it fresh. I love how the drafting stays gentle but still forces real trade-offs.",
                CreatedOn = reviewedOn
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                ReviewerId = alex.Id,
                Rating = 8.0,
                Description = "Great puzzle feel. The spatial constraints are satisfying and it never feels mean. I’d like a bit more tension, but it’s a great weeknight game.",
                CreatedOn = reviewedOn
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                ReviewerId = sam.Id,
                Rating = 7.0,
                Description = "Solid and relaxing. I enjoy the combos, but it can feel a touch samey if you play it back-to-back. Still a keeper.",
                CreatedOn = reviewedOn
            }
        ]);

        db.Games.AddRange(
        [
            cascadia,
            new BoardGameEntity
            {
                Id = Guid.NewGuid(),
                Name = "Azul",
                Status = 0,
                Tagline = "Draft tiles, build patterns, and try not to take what you can’t place.",
                ImageUrl = "images/placeholder-game-cover.svg"
            },
            new BoardGameEntity
            {
                Id = Guid.NewGuid(),
                Name = "Wingspan",
                Status = 0,
                Tagline = "A cozy engine-builder about birds with surprisingly crunchy decisions.",
                ImageUrl = "images/placeholder-game-cover.svg"
            },
            new BoardGameEntity
            {
                Id = Guid.NewGuid(),
                Name = "The Crew: Mission Deep Sea",
                Status = 1,
                Tagline = "A co-op trick-taking campaign with clever communication limits.",
                ImageUrl = "images/placeholder-game-cover.svg"
            },
            new BoardGameEntity
            {
                Id = Guid.NewGuid(),
                Name = "Spirit Island",
                Status = 1,
                Tagline = "Tense co-op defense with powerful combos and lots to master.",
                ImageUrl = "images/placeholder-game-cover.svg"
            },
            new BoardGameEntity
            {
                Id = Guid.NewGuid(),
                Name = "Heat: Pedal to the Metal",
                Status = 2,
                Tagline = "Push-your-luck racing with clean rules and exciting turns.",
                ImageUrl = "images/placeholder-game-cover.svg"
            },
            new BoardGameEntity
            {
                Id = Guid.NewGuid(),
                Name = "Dune: Imperium",
                Status = 2,
                Tagline = "Deck-building plus worker placement in a tight, tactical package.",
                ImageUrl = "images/placeholder-game-cover.svg"
            }
        ]);

        await db.SaveChangesAsync(ct);
    }

    private static async Task<MemberEntity> GetOrCreateMemberAsync(ApplicationDbContext db, string name, string? summary, CancellationToken ct)
    {
        var existing = await db.Members.FirstOrDefaultAsync(m => m.Name == name, ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new MemberEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = string.Empty,
            Summary = summary
        };

        db.Members.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }
}
