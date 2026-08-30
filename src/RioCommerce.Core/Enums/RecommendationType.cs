namespace RioCommerce.Core.Enums;

/// <summary>Category of an Offer / Combo recommendation, drives the section title shown on the storefront.</summary>
public enum RecommendationType
{
    StudentsAlsoAdded = 0,
    FrequentlyBoughtTogether = 1,
    CompleteYourPreparation = 2,
    CompleteYourCaKit = 3,
    RecommendedForYou = 4,
    Custom = 5
}
