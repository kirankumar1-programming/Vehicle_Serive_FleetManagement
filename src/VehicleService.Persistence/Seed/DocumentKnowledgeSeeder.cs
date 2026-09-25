using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VehicleService.Domain.Entities;
using VehicleService.Persistence.Context;

namespace VehicleService.Persistence.Seed;

public static class DocumentKnowledgeSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        if (await context.KnowledgeDocuments.AnyAsync())
        {
            return;
        }

        var documents = new List<KnowledgeDocument>
        {
            CreateMaintenanceGuide(),
            CreateWarrantyPolicy(),
            CreateRoadsideRescueManual(),
            CreateFleetSop(),
            CreateServiceCatalogAndPricing(),
            CreateWorkshopIntakeFaq()
        };

        foreach (var doc in documents)
        {
            // Auto chunk each document
            var chunks = ChunkDocument(doc);
            doc.ChunkCount = chunks.Count;
            foreach (var chunk in chunks)
            {
                doc.Chunks.Add(chunk);
            }
            context.KnowledgeDocuments.Add(doc);
        }

        await context.SaveChangesAsync();
    }

    private static KnowledgeDocument CreateMaintenanceGuide()
    {
        return new KnowledgeDocument
        {
            Title = "Automotive Maintenance & Scheduled Service Guide",
            Category = "Maintenance",
            Description = "Comprehensive guidelines on engine oil, brake systems, battery health, cooling, transmission, and periodic servicing intervals.",
            FileName = "Automotive_Maintenance_Guide.md",
            FileType = "text/markdown",
            RawContent = @"# Automotive Maintenance & Scheduled Service Guide

## Engine Oil & Filter Service
Engine oil lubricates internal engine parts, reduces friction, cools engine components, and suspends contaminants.
- **Service Interval**: Every 10,000 kilometers or 6 months (whichever comes first) for full synthetic oils (0W-20, 5W-30). Semi-synthetic oils require replacement every 7,500 kilometers.
- **Oil Filter**: Always replace the engine oil filter synchronously with every oil change. A clogged filter triggers the bypass valve, allowing unfiltered oil to circulate.
- **Signs of Degradation**: Dark gritty texture, ticking noise on cold startup, illuminated oil pressure warning light, or burning oil odor.

## Brake System Inspection & Maintenance
The braking system is the vehicle's primary active safety mechanism.
- **Brake Pad Thickness**: New brake pads typically have 10-12 mm of friction material. Minimum safe legal limit is 3.0 mm. Replacement is strongly recommended when pads reach 4.0 mm.
- **Brake Disc Rotors**: Inspect for deep scoring, heat discoloration, or thickness variation causing pedal pulsation. Minimum thickness is stamped on the rotor edge.
- **Brake Fluid (DOT 3 / DOT 4)**: Brake fluid is hygroscopic and absorbs ambient moisture over time, lowering its boiling point and causing spongy pedal feel. Complete brake fluid flush is required every 2 years or 40,000 km.

## Battery & Electrical System
The 12V auxiliary/starter battery powers vehicle electronics and cranks the starter motor.
- **Battery Health Parameters**: Full charge resting voltage is 12.6V to 12.8V. A voltage reading between 12.2V and 12.4V indicates 50% state of charge. Resting voltage below 12.0V indicates a discharged or sulfated battery requiring replacement.
- **Lifespan**: Average lead-acid and AGM battery lifespan is 3 to 4 years under normal operating temperatures.
- **Inspection Checklist**: Check for terminal corrosion (blue/white powder), clean with terminal protector spray, verify alternator charging output between 13.8V and 14.5V under load.

## Cooling System & Overheating Prevention
The engine cooling system prevents thermal distortion and head gasket failure.
- **Coolant Specifications**: Use manufacturer-specified organic acid technology (OAT/HOAT) ethylene glycol 50/50 pre-mix. Never add pure tap water as it causes mineral scaling and internal radiator corrosion.
- **Flush Interval**: Complete coolant drain, flush, and refill every 5 years or 100,000 km.
- **Emergency Overheating Steps**: Turn off air conditioning, switch heater to maximum heat to dissipate radiator core temperature, safely pull over, turn off engine, and wait at least 30 minutes before opening the expansion reservoir cap.

## Transmission & Drivetrain
- **Automatic Transmission Fluid (ATF)**: Inspect level and color every 20,000 km. Fresh ATF is bright translucent red. Burnt or brown fluid indicates internal clutch slip. Complete ATF fluid exchange is recommended every 60,000 to 80,000 km.
- **Manual Gearbox & Differential**: Hypoid gear oil replacement every 50,000 km or 3 years.
- **CV Boots & Drive Axles**: Inspect rubber constant velocity (CV) joint boots for tears, cracks, or grease leakage during every service lift inspection.

## Tires, Alignment & Suspension
- **Tire Tread Depth**: Legal minimum tread depth is 1.6 mm. Wet traction drastically diminishes below 3.0 mm. Use the built-in tread wear indicators (TWI).
- **Tire Rotation**: Rotate tires in front-to-rear or cross pattern every 10,000 km to ensure even wear across steering and drive axles.
- **Wheel Alignment & Balancing**: Recommended every 10,000 km or immediately if the steering pulls to one side or vibration occurs at highway speeds (80-110 km/h).
- **Shock Absorbers & Struts**: Inspect for oil weeping, torn dust covers, or excessive bouncing over speed bumps."
        };
    }

    private static KnowledgeDocument CreateWarrantyPolicy()
    {
        return new KnowledgeDocument
        {
            Title = "AutoPro Service Warranty & Genuine Parts Guarantee Policy",
            Category = "Warranty",
            Description = "Standard warranty coverage, terms, claim conditions, genuine OEM parts guarantee, and customer repair protections.",
            FileName = "Warranty_and_Guarantee_Policy.md",
            FileType = "text/markdown",
            RawContent = @"# AutoPro Service Warranty & Genuine Parts Guarantee Policy

## Standard Service Labor Warranty
AutoPro guarantees all workshop labor and mechanical workmanship for a period of **6 months or 10,000 kilometers**, whichever occurs first from the date of invoice issuance.
- If a mechanical defect, leak, or assembly error occurs directly relating to the labor performed, AutoPro will rectify the issue with zero additional labor charges.
- Customers must notify AutoPro within 7 calendar days of discovering the symptom.

## Genuine OEM Spare Parts Warranty
All replacement spare parts installed by AutoPro certified technicians are genuine OEM (Original Equipment Manufacturer) or premium OES certified parts.
- **Standard Parts Warranty**: 12 months or 20,000 kilometers comprehensive replacement warranty against manufacturing defects, premature failure, or material porosity.
- **Car Batteries**: 24 months to 36 months manufacturer warranty (standard 18-month full replacement + 18-month pro-rata credit depending on battery brand).
- **Brake Rotors & Calipers**: 12 months warranty against structural warping or cracking (excludes normal pad friction wear).

## Warranty Claim Process
To file a warranty claim:
1. Customer can initiate a claim via the Customer Portal under 'Warranties & Claims' or speak with an AutoPro Service Advisor.
2. Provide the original Invoice Number, Vehicle Registration Plate, or Warranty Number.
3. Bring the vehicle to any authorized AutoPro Service Center for diagnostic inspection.
4. If the diagnostic confirms failure of covered parts or workmanship, a Warranty Claim is authorized, and replacement is conducted with zero deductibles.

## Warranty Exclusions & Limitations
The warranty does not cover:
- Consumable friction wear and normal tear items such as wiper rubber blades, bulbs, brake friction pads (after 15,000 km), and clutch disc wear resulting from aggressive driving.
- Failures caused by vehicle misuse, racing, off-road driving beyond vehicle capability, water ingress/hydro-locking from driving through flooded roads.
- Modifications, performance chip tuning, or secondary repairs attempted by unauthorized outside third-party workshops without prior AutoPro inspection.
- Consequential commercial loss, rental car costs, or travel delays."
        };
    }

    private static KnowledgeDocument CreateRoadsideRescueManual()
    {
        return new KnowledgeDocument
        {
            Title = "24/7 Roadside Assistance & Emergency SOS Rescue Manual",
            Category = "Roadside",
            Description = "Standard operating procedures for on-spot assistance, breakdown triage, towing limits, flat tires, battery jump-starts, and emergency dispatch.",
            FileName = "Roadside_Assistance_Manual.md",
            FileType = "text/markdown",
            RawContent = @"# 24/7 Roadside Assistance & Emergency SOS Rescue Manual

## Emergency Dispatch Hotline & Digital SOS
AutoPro provides round-the-clock nationwide roadside assistance 365 days a year.
- **Toll-Free SOS Hotline**: **1800-AUTOPRO (1800-288-6776)**
- **Customer App SOS Button**: Tap 'Request Emergency Assistance' on the Customer Portal. It automatically transmits the vehicle's GPS coordinates, driver contact number, and registered vehicle details to the dispatch queue.

## On-Spot Assistance Services
1. **12V Battery Jump-Start**: High-capacity portable lithium jump packs and booster cables dispatched to start discharged batteries. If the battery is defective and unable to hold charge, on-spot replacement can be delivered.
2. **Flat Tire Change**: Mobile rescue technicians will mount the customer's vehicle spare wheel/donut tire and torque wheel nuts to manufacturer specifications. If spare is unavailable or flat, tire plug puncture repair is performed on-site.
3. **Emergency Fuel Delivery**: Up to 5 liters of free emergency petrol or diesel delivered to stranded vehicles to enable travel to the nearest fueling station.
4. **Lockout & Key Retrieval**: Non-destructive door unlocking service when keys are locked inside the passenger cabin.
5. **Minor Mechanical First-Aid**: Re-securing loose underbody plastic shields, radiator hose clamps, or replacing blown primary fuses.

## Towing Service & Coverage Limits
- **Coverage Radius**: Free flatbed towing up to **50 kilometers** to the nearest AutoPro authorized workshop. Beyond 50 km, an excess fee of $2.50 per kilometer applies.
- **Flatbed Transport**: Low-clearance performance vehicles, all-wheel-drive (AWD/4WD), and electric vehicles (EV) are transported strictly on hydraulic flatbed carriers to prevent drivetrain binding.

## Driver Safety Checklist During Roadside Breakdowns
- Safely steer the vehicle onto the shoulder or breakdown lane away from flowing traffic.
- Turn on hazard emergency flashers immediately.
- Turn vehicle steering wheels away from the roadway when stopped on an incline.
- Exit through the passenger side (away from traffic) and position all occupants behind the highway steel crash barrier.
- Deploy the red reflective warning triangle 50 meters behind the vehicle on urban roads, or 100 meters on highways."
        };
    }

    private static KnowledgeDocument CreateFleetSop()
    {
        return new KnowledgeDocument
        {
            Title = "Corporate Fleet Preventive Maintenance & Driver SOP",
            Category = "Fleet",
            Description = "Fleet maintenance scheduling, mileage inspection tiers, driver assignment rules, compliance, and downtime reduction protocols.",
            FileName = "Fleet_Preventive_Maintenance_SOP.md",
            FileType = "text/markdown",
            RawContent = @"# Corporate Fleet Preventive Maintenance & Driver SOP

## Fleet Preventive Maintenance (PM) Inspection Tiers
To maximize fleet uptime and preserve asset resale value, AutoPro enforces structured PM schedules for all commercial vans, trucks, and company cars:

- **PM-A Service (Every 5,000 km or 3 Months)**:
  - 25-point visual inspection: lights, horn, windshield wipers, tire pressures including spare.
  - Fluid top-up: washer fluid, brake fluid, engine coolant.
  - Visual check for undercarriage oil leaks and exhaust hanger integrity.
  - Typical turnaround: 45 minutes.

- **PM-B Service (Every 15,000 km or 6 Months)**:
  - Includes all PM-A tasks plus:
  - Full synthetic engine oil drain and OEM filter change.
  - Air filter cleaning or replacement, cabin pollen filter replacement.
  - 4-wheel brake pad measurement and caliper slide pin lubrication.
  - Tire rotation and dynamic wheel balancing.
  - Battery conductance test and terminal cleaning.
  - Typical turnaround: 2.5 hours.

- **PM-C Service (Every 30,000 km or 12 Months - Major Service)**:
  - Includes all PM-A and PM-B tasks plus:
  - Brake fluid hydraulic flush (DOT 4).
  - Transmission fluid and differential gear oil inspection/flush.
  - Spark plug replacement on gasoline models / fuel filter replacement on diesel models.
  - Full suspension bushing, ball joint, tie rod, and steering rack wear evaluation.
  - Computerized diagnostic health scan (OBD-II DTC scan) across all control modules.
  - Typical turnaround: 5 hours.

## Driver Assignment & Fleet Compliance Protocol
- Every commercial vehicle must have an active designated driver assigned in the Fleet Registry before dispatch.
- Drivers must hold an active commercial driving license verified within the past 12 months.
- Pre-Trip Inspection: Drivers must complete a daily digital 5-minute pre-trip walkaround log in the driver portal covering tires, fluid leaks, and warning lights.
- Overdue Servicing Lock: If a fleet vehicle exceeds its scheduled PM threshold by more than 1,000 km or 30 days, the portal flags the vehicle with an 'Overdue Servicing' alert to prevent dispatch until serviced."
        };
    }

    private static KnowledgeDocument CreateServiceCatalogAndPricing()
    {
        return new KnowledgeDocument
        {
            Title = "AutoPro Service Catalog, Packages & Transparent Pricing Guide",
            Category = "Pricing",
            Description = "Details of standard service packages, itemized labor and parts rates, turnaround times, and promo coupon guidelines.",
            FileName = "Service_Catalog_and_Pricing.md",
            FileType = "text/markdown",
            RawContent = @"# AutoPro Service Catalog, Packages & Transparent Pricing Guide

## Tiered Service Packages Overview

| Package Name | Starting Price | Turnaround Time | Inclusions |
| :--- | :--- | :--- | :--- |
| **Basic Lube Service** | **$99** | 1.5 - 2 Hours | Premium synthetic blend oil (up to 4.5L), new oil filter, 20-point safety check, wiper fluid top-up, battery health check |
| **Standard Care Package** | **$199** | 3 - 3.5 Hours | 100% Full synthetic oil (up to 5L), OEM oil filter, air filter replacement, 4-wheel brake inspection, tire rotation & pressure check, 40-point digital inspection with photo report |
| **Comprehensive Health Package** | **$349** | 4 - 5 Hours | Everything in Standard Package + cabin AC filter, spark plug inspection, brake caliper cleaning, coolant top-up, computerized OBD-II scan, interior vacuum & foam exterior wash |
| **Major Workshop Overhaul** | **$599+** | 1 Business Day | Comprehensive Package + transmission fluid flush, brake hydraulic fluid flush, cooling system flush, AC disinfection & refrigerant recharge, computerized 4-wheel laser alignment |

## Standard Workshop Labor & Specialized Diagnostic Rates
- Standard Mechanical Labor Rate: **$85 per hour** billed in 0.5-hour increments.
- Computerized OBD-II Diagnostic Scan: **$49 flat fee** (waived if repair work over $200 is approved).
- Wheel Alignment (4-Wheel Laser): **$65**.
- Brake Pad Replacement (Axle Pair, Labor only): **$75**.
- Air Conditioning Full Evacuation, Vacuum Test & R134a/R1234yf Recharge: **$140**.

## Promotional Discounts & Coupon Codes
- **WELCOME10**: 10% discount on all service packages for first-time customer vehicle registrations.
- **FLEETPRO**: 15% recurring volume discount for corporate fleet accounts with 5+ registered vehicles.
- **BRAKESAFE**: $25 off brake pad and rotor replacement packages during national road safety months."
        };
    }

    private static KnowledgeDocument CreateWorkshopIntakeFaq()
    {
        return new KnowledgeDocument
        {
            Title = "Workshop Intake, Digital Estimates, Billing & Customer FAQ",
            Category = "FAQ",
            Description = "Frequently asked questions regarding booking, digital inspection reports, estimate approval workflow, cancellation policies, and invoice payment.",
            FileName = "Workshop_Intake_and_Customer_FAQ.md",
            FileType = "text/markdown",
            RawContent = @"# Workshop Intake, Digital Estimates, Billing & Customer FAQ

## How does the Booking & Intake Process work?
1. **Online Appointment**: Customers select their vehicle, preferred service center, available service bay, and preferred date/time slot via the Customer Portal.
2. **Vehicle Check-In**: Upon arrival at the workshop, the Service Advisor conducts a walk-around check-in recording mileage, fuel level, and customer concern notes.
3. **40-Point Digital Inspection**: A technician performs a multi-point inspection with high-resolution photos documenting any component wear (Pass / Attention Required / Fail).

## Digital Repair Estimate Approval Workflow
- If additional repairs or worn components (e.g. worn brake pads or leaking suspension struts) are detected during inspection, the Service Advisor compiles an itemized **Digital Repair Estimate**.
- Customers receive an instant notification with a link to review the itemized parts and labor costs.
- **1-Click Approval or Rejection**: Customers can approve or reject the additional estimate from their phone or computer.
- **Rejection Policy**: If an estimate is rejected, the workshop continues and completes only the base authorized service. No penalty or surprise charges are added.

## Invoicing & Secure Payment Options
- Once servicing and quality control inspections are completed, an automated tax invoice is generated with clear itemization of labor, genuine parts, and applicable taxes.
- **Payment Methods**: Credit/Debit Cards, NetBanking, UPI, and Corporate Fleet Ledger accounts.
- **PDF Invoices**: Customers can view, download, and print official tax invoice receipts at any time from the Customer Invoices portal.

## Appointment Cancellation & Bay Release Policy
- Customers can cancel or reschedule appointments at any time up to **2 hours before the scheduled time slot** with zero cancellation fees.
- When an appointment is cancelled, the system atomically frees the reserved workshop bay and returns any reserved spare parts back to available inventory."
        };
    }

    private static List<DocumentChunk> ChunkDocument(KnowledgeDocument doc)
    {
        var chunks = new List<DocumentChunk>();
        var lines = doc.RawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        string currentSection = doc.Title;
        var sectionBuffer = new List<string>();
        int chunkIdx = 0;

        void FlushChunk()
        {
            if (sectionBuffer.Count == 0) return;
            string text = string.Join("\n", sectionBuffer).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            // Generate keywords from text
            var keywords = ExtractKeywords(text);

            chunks.Add(new DocumentChunk
            {
                ChunkIndex = chunkIdx++,
                SectionTitle = currentSection,
                ChunkText = text,
                Keywords = keywords
            });
            sectionBuffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## ") || line.StartsWith("# "))
            {
                FlushChunk();
                currentSection = line.TrimStart('#').Trim();
            }
            else
            {
                sectionBuffer.Add(line);
                // If section is very long, chunk at paragraph breaks
                if (sectionBuffer.Count >= 25 && string.IsNullOrWhiteSpace(line))
                {
                    FlushChunk();
                }
            }
        }
        FlushChunk();

        return chunks;
    }

    private static string ExtractKeywords(string text)
    {
        var words = text.ToLower()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '-', '(', ')', '[', ']', '/', '*', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Distinct()
            .Take(40);
        return string.Join(" ", words);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "is", "in", "to", "of", "it", "with", "for", "as", "on", "at", "by", "this", "that", "from",
        "are", "be", "or", "an", "will", "all", "any", "can", "has", "have", "had", "not", "but", "was", "were"
    };
}
