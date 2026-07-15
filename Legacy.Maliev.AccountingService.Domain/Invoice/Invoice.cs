using System;
using System.Collections.Generic;

namespace Legacy.Maliev.AccountingService.Domain.Invoice
{
    /// <summary>
    /// An invoice.
    /// </summary>
    public partial class Invoice
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Invoice"/> class.
        /// </summary>
        public Invoice()
        {
            InvoiceFiles = new HashSet<InvoiceFile>();
            InvoiceOrderItems = new HashSet<InvoiceOrderItem>();
        }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the number.
        /// </summary>
        /// <value>
        /// The number.
        /// </value>
        public string Number { get; set; }

        /// <summary>
        /// Gets or sets the customer identifier.
        /// </summary>
        /// <value>
        /// The customer identifier.
        /// </value>
        public int CustomerId { get; set; }

        /// <summary>
        /// Gets or sets the comment.
        /// </summary>
        /// <value>
        /// The comment.
        /// </value>
        public string Comment { get; set; }

        /// <summary>
        /// Gets or sets the internal comment.
        /// </summary>
        /// <value>
        /// The internal comment.
        /// </value>
        public string InternalComment { get; set; }

        /// <summary>
        /// Gets or sets the sales person.
        /// </summary>
        /// <value>
        /// The sales person.
        /// </value>
        public string SalesPerson { get; set; }

        /// <summary>
        /// Gets or sets the currency.
        /// </summary>
        /// <value>
        /// The currency.
        /// </value>
        public string Currency { get; set; }

        /// <summary>
        /// Gets or sets the purchase order number.
        /// </summary>
        /// <value>
        /// The purchase order number.
        /// </value>
        public string PurchaseOrderNumber { get; set; }

        /// <summary>
        /// Gets or sets the requisitioner.
        /// </summary>
        /// <value>
        /// The requisitioner.
        /// </value>
        public string Requisitioner { get; set; }

        /// <summary>
        /// Gets or sets the shipped via.
        /// </summary>
        /// <value>
        /// The shipped via.
        /// </value>
        public string ShippedVia { get; set; }

        /// <summary>
        /// Gets or sets the fob.
        /// </summary>
        /// <value>
        /// The fob.
        /// </value>
        public string Fob { get; set; }

        /// <summary>
        /// Gets or sets the terms.
        /// </summary>
        /// <value>
        /// The terms.
        /// </value>
        public string Terms { get; set; }

        /// <summary>
        /// Gets or sets the billing address recipient.
        /// </summary>
        /// <value>
        /// The billing address recipient.
        /// </value>
        public string BillingAddressRecipient { get; set; }

        /// <summary>
        /// Gets or sets the billing address company.
        /// </summary>
        /// <value>
        /// The billing address company.
        /// </value>
        public string BillingAddressCompany { get; set; }

        /// <summary>
        /// Gets or sets the billing address building.
        /// </summary>
        /// <value>
        /// The billing address building.
        /// </value>
        public string BillingAddressBuilding { get; set; }

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
        /// Gets or sets the billing address postal code.
        /// </summary>
        /// <value>
        /// The billing address postal code.
        /// </value>
        public string BillingAddressPostalCode { get; set; }

        /// <summary>
        /// Gets or sets the billing address country.
        /// </summary>
        /// <value>
        /// The billing address country.
        /// </value>
        public string BillingAddressCountry { get; set; }

        /// <summary>
        /// Gets or sets the shipping address recipient.
        /// </summary>
        /// <value>
        /// The shipping address recipient.
        /// </value>
        public string ShippingAddressRecipient { get; set; }

        /// <summary>
        /// Gets or sets the shipping address recipient telephone.
        /// </summary>
        /// <value>
        /// The shipping address recipient telephone.
        /// </value>
        public string ShippingAddressRecipientTelephone { get; set; }

        /// <summary>
        /// Gets or sets the shipping address company.
        /// </summary>
        /// <value>
        /// The shipping address company.
        /// </value>
        public string ShippingAddressCompany { get; set; }

        /// <summary>
        /// Gets or sets the shipping address building.
        /// </summary>
        /// <value>
        /// The shipping address building.
        /// </value>
        public string ShippingAddressBuilding { get; set; }

        /// <summary>
        /// Gets or sets the shipping address line1.
        /// </summary>
        /// <value>
        /// The shipping address line1.
        /// </value>
        public string ShippingAddressLine1 { get; set; }

        /// <summary>
        /// Gets or sets the shipping address line2.
        /// </summary>
        /// <value>
        /// The shipping address line2.
        /// </value>
        public string ShippingAddressLine2 { get; set; }

        /// <summary>
        /// Gets or sets the shipping address city.
        /// </summary>
        /// <value>
        /// The shipping address city.
        /// </value>
        public string ShippingAddressCity { get; set; }

        /// <summary>
        /// Gets or sets the shipping address state.
        /// </summary>
        /// <value>
        /// The shipping address state.
        /// </value>
        public string ShippingAddressState { get; set; }

        /// <summary>
        /// Gets or sets the shipping address postal code.
        /// </summary>
        /// <value>
        /// The shipping address postal code.
        /// </value>
        public string ShippingAddressPostalCode { get; set; }

        /// <summary>
        /// Gets or sets the shipping address country.
        /// </summary>
        /// <value>
        /// The shipping address country.
        /// </value>
        public string ShippingAddressCountry { get; set; }

        /// <summary>
        /// Gets or sets the commercial registration.
        /// </summary>
        /// <value>
        /// The commercial registration.
        /// </value>
        public string CommercialRegistration { get; set; }

        /// <summary>
        /// Gets or sets the tax identification.
        /// </summary>
        /// <value>
        /// The tax identification.
        /// </value>
        public string TaxIdentification { get; set; }

        /// <summary>
        /// Gets or sets the subtotal.
        /// </summary>
        /// <value>
        /// The subtotal.
        /// </value>
        public decimal? Subtotal { get; set; }

        /// <summary>
        /// Gets or sets the vat.
        /// </summary>
        /// <value>
        /// The vat.
        /// </value>
        public decimal? Vat { get; set; }

        /// <summary>
        /// Gets or sets the total.
        /// </summary>
        /// <value>
        /// The total.
        /// </value>
        public decimal? Total { get; set; }

        /// <summary>
        /// Gets or sets the withholding tax.
        /// </summary>
        /// <value>
        /// The withholding tax.
        /// </value>
        public decimal? WithholdingTax { get; set; }

        /// <summary>
        /// Gets or sets the outstanding.
        /// </summary>
        /// <value>
        /// The outstanding.
        /// </value>
        public decimal? Outstanding { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is paid.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is paid; otherwise, <c>false</c>.
        /// </value>
        public bool IsPaid { get; set; }

        /// <summary>
        /// Gets or sets the receipt identifier.
        /// </summary>
        /// <value>
        /// The receipt identifier.
        /// </value>
        public int? ReceiptId { get; set; }

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
        /// Gets or sets the invoice files.
        /// </summary>
        /// <value>
        /// The invoice files.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<InvoiceFile> InvoiceFiles { get; set; }

        /// <summary>
        /// Gets or sets the order items.
        /// </summary>
        /// <value>
        /// The order items.
        /// </value>
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<InvoiceOrderItem> InvoiceOrderItems { get; set; }
    }
}
