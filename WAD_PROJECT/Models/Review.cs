using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgriDirect.Models
{
    public class Review
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order? Order { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        public int FarmerId { get; set; }

        [ForeignKey("FarmerId")]
        public virtual User? Farmer { get; set; }

        [Required]
        public int BuyerId { get; set; }

        [ForeignKey("BuyerId")]
        public virtual User? Buyer { get; set; }

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; } // 1 to 5 stars

        [Required, MaxLength(1000)]
        public string Comment { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
