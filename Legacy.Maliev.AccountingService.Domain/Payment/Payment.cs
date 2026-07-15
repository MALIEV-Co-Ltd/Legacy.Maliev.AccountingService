namespace Legacy.Maliev.AccountingService.Domain.Payment
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A payment.
    /// </summary>
    public partial class Payment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Payment"/> class.
        /// </summary>
        public Payment()
        {
            this.PaymentFile = new HashSet<PaymentFile>();
        }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the employee identifier.
        /// </summary>
        /// <value>
        /// The employee identifier.
        /// </value>
        public int? EmployeeId { get; set; }

        /// <summary>
        /// Gets or sets the payment direction identifier.
        /// </summary>
        /// <value>
        /// The payment direction identifier.
        /// </value>
        public int PaymentDirectionId { get; set; }

        /// <summary>
        /// Gets or sets the payment type identifier.
        /// </summary>
        /// <value>
        /// The payment type identifier.
        /// </value>
        public int PaymentTypeId { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        /// <value>
        /// The description.
        /// </value>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the payment method identifier.
        /// </summary>
        /// <value>
        /// The payment method identifier.
        /// </value>
        public int PaymentMethodId { get; set; }

        /// <summary>
        /// Gets or sets the amount.
        /// </summary>
        /// <value>
        /// The amount.
        /// </value>
        public decimal Amount { get; set; }

        /// <summary>
        /// Gets or sets the currency identifier.
        /// </summary>
        /// <value>
        /// The currency identifier.
        /// </value>
        public int? CurrencyId { get; set; }

        /// <summary>
        /// Gets or sets the recipient.
        /// </summary>
        /// <value>
        /// The recipient.
        /// </value>
        public string Recipient { get; set; }

        /// <summary>
        /// Gets or sets the transaction number.
        /// </summary>
        /// <value>
        /// The transaction number.
        /// </value>
        public string TransactionNumber { get; set; }

        /// <summary>
        /// Gets or sets the payment date.
        /// </summary>
        /// <value>
        /// The payment date.
        /// </value>
        public DateTime? PaymentDate { get; set; }

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
        /// Gets or sets the payment direction.
        /// </summary>
        /// <value>
        /// The payment direction.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual PaymentDirection PaymentDirection { get; set; }

        /// <summary>
        /// Gets or sets the payment method.
        /// </summary>
        /// <value>
        /// The payment method.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual PaymentMethod PaymentMethod { get; set; }

        /// <summary>
        /// Gets or sets the type of the payment.
        /// </summary>
        /// <value>
        /// The type of the payment.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual PaymentType PaymentType { get; set; }

        /// <summary>
        /// Gets or sets the payment file.
        /// </summary>
        /// <value>
        /// The payment file.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<PaymentFile> PaymentFile { get; set; }
    }
}
