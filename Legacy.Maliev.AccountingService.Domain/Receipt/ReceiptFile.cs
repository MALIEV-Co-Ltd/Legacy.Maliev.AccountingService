namespace Legacy.Maliev.AccountingService.Domain.Receipt
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Receipt file.
    /// </summary>
    public partial class ReceiptFile
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the receipt identifier.
        /// </summary>
        /// <value>
        /// The receipt identifier.
        /// </value>
        public int ReceiptId { get; set; }

        /// <summary>
        /// Gets or sets the bucket.
        /// </summary>
        /// <value>
        /// The bucket.
        /// </value>
        public string Bucket { get; set; }

        /// <summary>
        /// Gets or sets the name of the object.
        /// </summary>
        /// <value>
        /// The name of the object.
        /// </value>
        public string ObjectName { get; set; }

        /// <summary>
        /// Gets or sets the created date.
        /// </summary>
        /// <value>
        /// The created date.
        /// </value>
        public DateTime? CreatedDate { get; set; }

        /// <summary>
        /// Gets or sets the modified date.
        /// </summary>
        /// <value>
        /// The modified date.
        /// </value>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// Gets or sets the receipt.
        /// </summary>
        /// <value>
        /// The receipt.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual Receipt Receipt { get; set; }
    }
}
