using BoardGameMondays.Core;
using Microsoft.AspNetCore.Identity;

namespace BoardGameMondays.Data;

public sealed class ApplicationUser : IdentityUser
{
	public int BgmCoins { get; set; } = 100;

	/// <summary>
	/// User's preferred format for displaying betting odds.
	/// </summary>
	public OddsDisplayFormat OddsDisplayFormat { get; set; } = OddsDisplayFormat.Fraction;
}
