namespace Ehrenmeter.Backend.Models
{
    internal class EhreTransaction
    {
        public required User Giver { get; set; }
        public required User Receiver { get; set; }
        public required int Amount { get; set; }
        public string? Description { get; set; }
        public required DateTime TransactionDate { get; set; }
    }
}
