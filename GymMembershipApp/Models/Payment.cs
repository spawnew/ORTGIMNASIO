using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymMembershipApp.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Member is required")]
        public int MemberId { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; }

        [Required(ErrorMessage = "Payment method is required")]
        public PaymentMethod PaymentMethod { get; set; }

        [Required(ErrorMessage = "Status is required")]
        public PaymentStatus Status { get; set; }

        public string? TransactionReference { get; set; }

        public string? Notes { get; set; }

        public int? MembershipPlanId { get; set; }

        // Navigation properties
        [ForeignKey("MemberId")]
        public Member? Member { get; set; }    // <- cambiado a nullable

        [ForeignKey("MembershipPlanId")]
        public MembershipPlan? MembershipPlan { get; set; }
    }

    public enum PaymentMethod
    {
        Cash = 0,
        CreditCard = 1,
        DebitCard = 2,
        BankTransfer = 3,
        Other = 4
    }

    public enum PaymentStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2,
        Refunded = 3,
        Cancelled = 4
    }
}