namespace Legacy.Maliev.AccountingService.Domain.Receipt
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents an order item within a receipt.
    /// </summary>
    public partial class ReceiptOrderItem
    {
        /// <summary>
        /// Gets or sets the unique identifier for the order item.
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Gets or sets the foreign key to the associated receipt.
        /// </summary>
        public int? ReceiptId { get; set; }
        /// <summary>
        /// Gets or sets the description of the order item.
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// Gets or sets the quantity of the item.
        /// </summary>
        public int? Quantity { get; set; }
        /// <summary>
        /// Gets or sets the unit price of the item.
        /// </summary>
        public decimal? UnitPrice { get; set; }
        /// <summary>
        /// Gets or sets the subtotal for the order item (Quantity * UnitPrice).
        /// </summary>
        public decimal? Subtotal { get; set; }
        /// <summary>
        /// Gets or sets the date and time when the order item was created.
        /// </summary>
        public DateTime? CreatedDate { get; set; }
        /// <summary>
        /// Gets or sets the date and time when the order item was last modified.
        /// </summary>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// Gets or sets the navigation property to the associated Receipt.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual Receipt Receipt { get; set; }
    }
}
