namespace RioCommerce.Core.Enums;

// How tax is treated relative to listed prices (used by finance configuration).
public enum TaxMode { Exclusive, Inclusive }

// Rounding applied to computed money amounts. Nearest/Up/Down operate on whole rupees.
public enum RoundingMode { None, Nearest, Up, Down }

// Cadence a future auto-settlement scheduler uses to generate payout batches.
public enum PayoutFrequency { Manual, Weekly, Fortnightly, Monthly }
