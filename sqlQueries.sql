-- Find El Grande
SELECT Id, Name, ImageUrl FROM Games WHERE Name LIKE '%Grande%';

-- Check votes
SELECT * FROM WantToPlayVotes;

-- Check if vote GameIds exist in Games table
SELECT 
    v.GameId,
    v.UserId,
    v.CreatedOn,
    g.Id AS MatchedGameId,
    g.Name
FROM WantToPlayVotes v
LEFT JOIN Games g ON v.GameId = g.Id
ORDER BY v.CreatedOn DESC;

SELECT Id, UserName, Email, EmailConfirmed
FROM AspNetUsers
ORDER BY UserName;
