namespace Legacy.Maliev.AccountingService.Domain.Payment
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Payment file.
    /// </summary>
    public partial class PaymentFile
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the payment identifier.
        /// </summary>
        /// <value>
        /// The payment identifier.
        /// </value>
        public int PaymentId { get; set; }

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
        /// Gets or sets the payment.
        /// </summary>
        /// <value>
        /// The payment.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public Payment Payment { get; set; }
    }
}
