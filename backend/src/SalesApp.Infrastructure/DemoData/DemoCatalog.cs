using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.DemoData;

// Fictional reference data for the demo generator: a textile trading business in Tamil Nadu selling
// yarn, fabric, home textiles, garments and packing material. Every name is made up.

internal enum Segment
{
    Knitter,
    Weaver,
    GarmentMaker,
    Wholesaler,
    Retailer,
}

internal sealed record ProductSpec(
    string Sku,
    string Name,
    string Category,
    string Hsn,
    ProductUnit Unit,
    decimal Price,
    decimal CostRatio,
    double Weight,
    decimal QtyMin,
    decimal QtyMax,
    decimal Step,
    Segment[] BoughtBy);

internal sealed record CitySpec(string City, string StateCode, int PincodeBase, double Weight, Segment[] Segments);

internal static class DemoCatalog
{
    public const string CottonYarn = "Cotton yarn";
    public const string BlendedYarn = "Synthetic & blended yarn";
    public const string Fabrics = "Fabrics";
    public const string HomeTextiles = "Home textiles";
    public const string Garments = "Garments";
    public const string Packing = "Packing material";

    private static readonly Segment[] YarnBuyers = [Segment.Knitter, Segment.Weaver];
    private static readonly Segment[] FabricBuyers = [Segment.GarmentMaker, Segment.Wholesaler];
    private static readonly Segment[] FinishedGoodsBuyers = [Segment.Wholesaler, Segment.Retailer];
    private static readonly Segment[] Everyone = [Segment.Knitter, Segment.Weaver, Segment.GarmentMaker];

    /// <summary>HSN codes the catalogue needs beyond the starter set seeded by migration.</summary>
    public static readonly (string Code, string Description, decimal Rate)[] ExtraHsnCodes =
    [
        ("5209", "Woven fabrics of cotton, 85% or more cotton, over 200 g/m2", 5m),
        ("6006", "Other knitted or crocheted fabrics", 5m),
    ];

    public static readonly ProductSpec[] Products =
    [
        new("CY-20K", "Cotton yarn 20s carded", CottonYarn, "5205", ProductUnit.Kg, 255m, 0.86m, 1.0, 200, 1500, 50, YarnBuyers),
        new("CY-30K", "Cotton yarn 30s carded", CottonYarn, "5205", ProductUnit.Kg, 275m, 0.86m, 1.2, 200, 1500, 50, YarnBuyers),
        new("CY-30C", "Cotton yarn 30s combed", CottonYarn, "5205", ProductUnit.Kg, 298m, 0.85m, 1.4, 200, 1200, 50, YarnBuyers),
        new("CY-40C", "Cotton yarn 40s combed", CottonYarn, "5205", ProductUnit.Kg, 315m, 0.85m, 1.5, 200, 1200, 50, YarnBuyers),
        new("CY-60C", "Cotton yarn 60s combed compact", CottonYarn, "5205", ProductUnit.Kg, 385m, 0.84m, 0.7, 100, 600, 25, YarnBuyers),
        new("CY-80C", "Cotton yarn 80s combed compact", CottonYarn, "5205", ProductUnit.Kg, 455m, 0.83m, 0.4, 50, 400, 25, YarnBuyers),
        new("PV-30", "Polyester-viscose yarn 30s (65/35)", BlendedYarn, "5509", ProductUnit.Kg, 205m, 0.87m, 0.9, 200, 1200, 50, YarnBuyers),
        new("PC-40", "Poly-cotton yarn 40s (52/48)", BlendedYarn, "5206", ProductUnit.Kg, 245m, 0.86m, 0.8, 200, 1000, 50, YarnBuyers),
        new("PSF-30", "Polyester spun yarn 30s", BlendedYarn, "5509", ProductUnit.Kg, 175m, 0.88m, 0.6, 200, 1200, 50, YarnBuyers),
        new("VSY-30", "Viscose spun yarn 30s", BlendedYarn, "5509", ProductUnit.Kg, 230m, 0.87m, 0.5, 100, 800, 50, YarnBuyers),
        new("FB-POP", "Cotton poplin 60x60, 58 inch", Fabrics, "5208", ProductUnit.Metre, 92m, 0.82m, 1.0, 500, 4000, 100, FabricBuyers),
        new("FB-CAM", "Cotton cambric 60x60, 44 inch", Fabrics, "5208", ProductUnit.Metre, 68m, 0.82m, 0.7, 500, 3000, 100, FabricBuyers),
        new("FB-TWL", "Cotton twill 2/1, 58 inch", Fabrics, "5209", ProductUnit.Metre, 145m, 0.80m, 0.8, 300, 2500, 100, FabricBuyers),
        new("FB-DNM", "Denim 10 oz, 58 inch", Fabrics, "5209", ProductUnit.Metre, 210m, 0.80m, 0.7, 300, 2000, 100, FabricBuyers),
        new("FB-JRS", "Single jersey knitted fabric 160 gsm", Fabrics, "6006", ProductUnit.Kg, 340m, 0.82m, 0.8, 100, 800, 25, FabricBuyers),
        new("FB-SAT", "Polyester satin, 58 inch", Fabrics, "5407", ProductUnit.Metre, 75m, 0.84m, 0.5, 500, 3000, 100, FabricBuyers),
        new("HT-BSD", "Cotton double bedsheet set (1+2)", HomeTextiles, "6302", ProductUnit.Set, 645m, 0.78m, 0.9, 20, 300, 10, FinishedGoodsBuyers),
        new("HT-BSS", "Cotton single bedsheet set (1+1)", HomeTextiles, "6302", ProductUnit.Set, 420m, 0.78m, 0.6, 20, 250, 10, FinishedGoodsBuyers),
        new("HT-TWL", "Terry towel 500 gsm, 30x60 inch", HomeTextiles, "6302", ProductUnit.Piece, 185m, 0.77m, 0.8, 50, 600, 25, FinishedGoodsBuyers),
        new("HT-KIT", "Kitchen towel, pack of 6", HomeTextiles, "6302", ProductUnit.Set, 210m, 0.76m, 0.4, 25, 300, 25, FinishedGoodsBuyers),
        new("HT-PIL", "Pillow cover pair", HomeTextiles, "6302", ProductUnit.Set, 160m, 0.76m, 0.4, 25, 300, 25, FinishedGoodsBuyers),
        new("GT-TSR", "Round-neck cotton T-shirt", Garments, "6109", ProductUnit.Piece, 145m, 0.78m, 1.0, 100, 2000, 50, FinishedGoodsBuyers),
        new("GT-TSP", "Polo T-shirt, pique", Garments, "6109", ProductUnit.Piece, 265m, 0.77m, 0.6, 50, 1000, 50, FinishedGoodsBuyers),
        new("GT-VST", "Cotton vest, pack of 3", Garments, "6109", ProductUnit.Set, 210m, 0.78m, 0.5, 50, 800, 25, FinishedGoodsBuyers),
        new("PK-CON", "Plastic yarn cone, box of 100", Packing, "3923", ProductUnit.Box, 950m, 0.80m, 0.5, 5, 60, 5, Everyone),
        new("PK-BAG", "HDPE packing bag 30x40, box of 100", Packing, "3923", ProductUnit.Box, 780m, 0.80m, 0.4, 5, 50, 5, Everyone),
    ];

    public static readonly CitySpec[] Cities =
    [
        new("Tiruppur", "33", 641601, 0.24, [Segment.Knitter, Segment.GarmentMaker]),
        new("Erode", "33", 638001, 0.09, [Segment.Weaver]),
        new("Karur", "33", 639001, 0.07, [Segment.Weaver, Segment.Wholesaler]),
        new("Coimbatore", "33", 641002, 0.09, [Segment.Weaver, Segment.Knitter]),
        new("Salem", "33", 636001, 0.05, [Segment.Weaver]),
        new("Madurai", "33", 625001, 0.04, [Segment.Retailer]),
        new("Chennai", "33", 600001, 0.06, [Segment.Wholesaler, Segment.Retailer]),
        new("Bengaluru", "29", 560001, 0.06, [Segment.GarmentMaker, Segment.Retailer]),
        new("Surat", "24", 395001, 0.05, [Segment.Weaver, Segment.Wholesaler]),
        new("Ludhiana", "03", 141001, 0.04, [Segment.Knitter]),
        new("Bhiwandi", "27", 421302, 0.04, [Segment.Weaver]),
        new("Mumbai", "27", 400002, 0.03, [Segment.Wholesaler]),
        new("Kolkata", "19", 700001, 0.03, [Segment.Wholesaler]),
        new("New Delhi", "07", 110001, 0.04, [Segment.Wholesaler, Segment.Retailer]),
        new("Hyderabad", "36", 500001, 0.03, [Segment.Retailer]),
        new("Kochi", "32", 682001, 0.02, [Segment.Retailer]),
        new("Puducherry", "34", 605001, 0.02, [Segment.Retailer]),
    ];

    public static readonly string[] NamePrefixes =
    [
        "Sri", "Shree", "New", "Royal", "Golden", "Lakshmi", "Murugan", "Amman", "Balaji", "Ganesh", "Kaveri", "Annapoorna",
        "Sakthi", "Vel", "Saraswathi", "Mahalakshmi", "Bharath", "Om", "Kumaran", "Pandian", "Thangam", "Vijaya", "Selvam", "Kongu",
    ];

    public static readonly Dictionary<Segment, string[]> NameCores = new()
    {
        [Segment.Knitter] = ["Knit Fabrics", "Knitting Mills", "Hosiery", "Knits"],
        [Segment.Weaver] = ["Weaves", "Textiles", "Weaving Mills", "Fabrics"],
        [Segment.GarmentMaker] = ["Garments", "Apparels", "Exports", "Fashions"],
        [Segment.Wholesaler] = ["Traders", "Textile Agencies", "Cloth Merchants", "Distributors"],
        [Segment.Retailer] = ["Home Linen", "Textile Mart", "Silks & Linen", "Stores"],
    };

    public static readonly string[] NameSuffixes = ["", "", "Pvt Ltd", "& Co", "Enterprises", "Industries", "LLP"];

    public static readonly string[] ContactNames =
    [
        "R. Senthil Kumar", "K. Priya", "M. Ramesh", "S. Lakshmi", "V. Karthik", "P. Anitha", "N. Suresh", "G. Revathi",
        "A. Imran", "T. Deepa", "B. Prakash", "J. Mohan", "Harpreet Singh", "Rakesh Patel", "Sanjay Gupta", "Sourav Das",
    ];

    public static readonly string[] Suppliers =
    [
        "Lakshmi Spinning Mills", "Kongu Yarn Agencies", "Coimbatore Cotton Corporation", "Erode Fabric Processors",
        "Karur Home Textile Makers", "Tiruppur Garment Works", "Sivakasi Packaging Industries",
    ];

    /// <summary>Relative demand by calendar month: festive Navratri/Diwali peak in Oct-Nov, March year-end push, April-May lull.</summary>
    public static readonly double[] Seasonality = [0.95, 0.90, 1.05, 0.85, 0.85, 0.90, 0.95, 1.05, 1.15, 1.45, 1.35, 1.00];

    public static readonly string[] CancelReasons =
    [
        "Customer cancelled the requirement", "Price not agreed", "Duplicate order", "Delivery timeline not suitable",
    ];
}
