namespace Legacy.Maliev.AccountingService.Domain.Payment
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Payment method.
    /// </summary>
    public partial class PaymentMethod
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PaymentMethod"/> class.
        /// </summary>
        public PaymentMethod()
        {
            this.Payment = new HashSet<Payment>();
        }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>
        /// The name.
        /// </value>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        /// <value>
        /// The description.
        /// </value>
        public string Description { get; set; }

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
        public ICollection<Payment> Payment { get; set; }
    }
}
