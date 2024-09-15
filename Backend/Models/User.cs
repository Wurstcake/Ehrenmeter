namespace Ehrenmeter.Backend.Models
{
    internal class User
    {
        public required int UserId { get; set; }
        public required string Username { get; set; }
        public int? Ehre { get; set; }
    }
}
