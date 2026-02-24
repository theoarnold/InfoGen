using Microsoft.AspNetCore.Identity;

namespace InfoGen.Data
{
    public class ApplicationUser : IdentityUser
    {
        public string? StripeCustomerId { get; set; }
    }
}
