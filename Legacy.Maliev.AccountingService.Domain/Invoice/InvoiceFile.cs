using System;
using System.Collections.Generic;

namespace Legacy.Maliev.AccountingService.Domain.Invoice
{
    /// <summary>
    /// An invoice file.
    /// </summary>
    public partial class InvoiceFile
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the invoice identifier.
        /// </summary>
        /// <value>
        /// The invoice identifier.
        /// </value>
        public int InvoiceId { get; set; }

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
        /// Gets or sets the invoice.
        /// </summary>
        /// <value>
        /// The invoice.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual Invoice Invoice { get; set; }
    }
}
