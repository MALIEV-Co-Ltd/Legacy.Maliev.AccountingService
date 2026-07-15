namespace Legacy.Maliev.AccountingService.Domain.Receipt
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A receipt.
    /// </summary>
    public partial class Receipt
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Receipt"/> class.
        /// </summary>
        public Receipt()
        {
            this.ReceiptOrderItem = new HashSet<ReceiptOrderItem>();
            this.ReceiptFile = new HashSet<ReceiptFile>();
        }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the customer identifier.
        /// </summary>
        /// <value>
        /// The customer identifier.
        /// </value>
        public int? CustomerId { get; set; }

        /// <summary>
        /// Gets or sets the invoice number.
        /// </summary>
        /// <value>
        /// The invoice number.
        /// </value>
        public string InvoiceNumber { get; set; }

        /// <summary>
        /// Gets or sets the payment date.
        /// </summary>
        /// <value>
        /// The payment date.
        /// </value>
        public DateTime PaymentDate { get; set; }

        /// <summary>
        /// Gets or sets the currency.
        /// </summary>
        /// <value>
        /// The currency.
        /// </value>
        public string Currency { get; set; }

        /// <summary>
        /// Gets or sets the subtotal.
        /// </summary>
        /// <value>
        /// The subtotal.
        /// </value>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// Gets or sets the withholding tax.
        /// </summary>
        /// <value>
        /// The withholding tax.
        /// </value>
        public decimal? WithholdingTax { get; set; }

        /// <summary>
        /// Gets or sets the amount paid.
        /// </summary>
        /// <value>
        /// The amount paid.
        /// </value>
        public decimal? AmountPaid { get; set; }

        /// <summary>
        /// Gets or sets the vat.
        /// </summary>
        /// <value>
        /// The vat.
        /// </value>
        public decimal Vat { get; set; }

        /// <summary>
        /// Gets or sets the total.
        /// </summary>
        /// <value>
        /// The total.
        /// </value>
        public decimal Total { get; set; }

        /// <summary>
        /// Gets or sets the comment.
        /// </summary>
        /// <value>
        /// The comment.
        /// </value>
        public string Comment { get; set; }

        /// <summary>
        /// Gets or sets the tax identification.
        /// </summary>
        /// <value>
        /// The tax identification.
        /// </value>
        public string TaxIdentification { get; set; }

        /// <summary>
        /// Gets or sets the commercial registration.
        /// </summary>
        /// <value>
        /// The commercial registration.
        /// </value>
        public string CommercialRegistration { get; set; }

        /// <summary>
        /// Gets or sets the billing address building.
        /// </summary>
        /// <value>
        /// The billing address building.
        /// </value>
        public string BillingAddressBuilding { get; set; }

        /// <summary>
        /// Gets or sets the billing address company.
        /// </summary>
        /// <value>
        /// The billing address company.
        /// </value>
        public string BillingAddressCompany { get; set; }

        /// <summary>
        /// Gets or sets the billing address recipient.
        /// </summary>
        /// <value>
        /// The billing address recipient.
        /// </value>
        public string BillingAddressRecipient { get; set; }

        /// <summary>
        /// Gets or sets the billing address line1.
        /// </summary>
        /// <value>
        /// The billing address line1.
        /// </value>
        public string BillingAddressLine1 { get; set; }

        /// <summary>
        /// Gets or sets the billing address line2.
        /// </summary>
        /// <value>
        /// The billing address line2.
        /// </value>
        public string BillingAddressLine2 { get; set; }

        /// <summary>
        /// Gets or sets the billing address city.
        /// </summary>
        /// <value>
        /// The billing address city.
        /// </value>
        public string BillingAddressCity { get; set; }

        /// <summary>
        /// Gets or sets the billing address state.
        /// </summary>
        /// <value>
        /// The billing address state.
        /// </value>
        public string BillingAddressState { get; set; }

        /// <summary>
        /// Gets or sets the billing address country.
        /// </summary>
        /// <value>
        /// The billing address country.
        /// </value>
        public string BillingAddressCountry { get; set; }

        /// <summary>
        /// Gets or sets the billing address postal code.
        /// </summary>
        /// <value>
        /// The billing address postal code.
        /// </value>
        public string BillingAddressPostalCode { get; set; }

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
        /// Gets or sets the order item.
        /// </summary>
        /// <value>
        /// The order item.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<ReceiptOrderItem> ReceiptOrderItem { get; set; }

        /// <summary>
        /// Gets or sets the receipt file.
        /// </summary>
        /// <value>
        /// The receipt file.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<ReceiptFile> ReceiptFile { get; set; }
    }
}
