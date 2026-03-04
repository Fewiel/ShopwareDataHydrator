# ShopwareDataHydrator

Console application for populating a Shopware 6 instance with realistic test data (customers and orders) via the API. Primarily designed to fill Shopware Analytics with meaningful data. Also useful for testing ERP/WaWi integrations, since all orders are placed through the regular Storefront API — just like real customer orders.

## Quick Start (Pre-built Binaries)

Download the latest release for your platform from the [Releases](../../releases) page. No .NET SDK or runtime required — the binaries are fully self-contained.

| Platform        | File                                      |
|-----------------|-------------------------------------------|
| Linux (x64)     | `ShopwareDataHydrator-linux-x64`     |
| Windows (x64)   | `ShopwareDataHydrator-win-x64.exe`   |
| macOS (ARM)     | `ShopwareDataHydrator-osx-arm64`     |

**Linux / macOS:**

```bash
chmod +x ShopwareDataHydrator-linux-x64
./ShopwareDataHydrator-linux-x64 -url=https://myshop.com -user=admin -password=shopware
```

**Windows:**

```cmd
ShopwareDataHydrator-win-x64.exe -url=https://myshop.com -user=admin -password=shopware
```

## Features

- Creates customers with realistic locale-appropriate names and addresses (via [Bogus](https://github.com/bchavez/Bogus))
- Randomly uses different countries available in the sales channel with matching locale data
- Randomly uses different payment and shipping methods available in the sales channel
- Creates orders for registered customers and guest customers via the Storefront API
- Uses existing products from the sales channel
- Backdates orders randomly across a configurable time range
- Sets various order statuses (Open, In Progress, Completed, Cancelled, Refunded)
- Applies available promotion codes to a portion of orders
- Live mode for simulating real-time order traffic (ERP/WaWi testing)
- Suppresses email sending on state transitions (`sendMail: false`)
- Runs on Linux, Windows and macOS

## Prerequisites

- Shopware 6 instance with admin access (username/password)
- At least one Storefront sales channel with available products

## Build from Source (optional)

Only required if you want to build from source instead of using the pre-built binaries.

Requires [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build
```

## Usage

```bash
dotnet run -- -url=<shop-url> -user=<admin> -password=<pass> [options]
```

### Required Parameters

| Parameter    | Description            |
|-------------|------------------------|
| `-url`      | Shopware 6 shop URL    |
| `-user`     | Admin username         |
| `-password` | Admin password         |

### Optional Parameters

| Parameter         | Default | Description                                        |
|-------------------|--------:|----------------------------------------------------|
| `-customers`      |      10 | Number of customers to create                      |
| `-orders`         |      50 | Number of orders to create                         |
| `-days`           |     365 | Backdate range in days from today                  |
| `-failed-orders`  |       5 | Percentage of cancelled/refunded orders            |
| `-sales-channel`  |       — | Sales channel name (default: first active storefront) |
| `-live`           |       — | Live mode: spread orders over given duration (seconds) |
| `-live-status`    |       0 | Apply status transitions in live mode (`1` = on)     |
| `-help`           |       — | Show help                                          |

> **Note:** Values with spaces must be quoted, e.g. `-sales-channel="Storefront DE"`

### Batch Mode (default)

Creates all orders as fast as possible, backdates them across the `-days` range, and applies status transitions.

```bash
dotnet run -- -url=https://myshop.com -user=admin -password=shopware \
  -customers=50 -orders=200 -days=180 -sales-channel="Storefront DE"
```

### Live Mode

Distributes orders evenly over a given duration, simulating real-time customer traffic. Orders keep their current timestamp (no backdating). Useful for testing ERP/WaWi integrations that react to incoming orders.

Status transitions are off by default so orders arrive as "Open". Enable with `-live-status=1`.

```bash
dotnet run -- -url=https://myshop.com -user=admin -password=shopware \
  -customers=10 -orders=30 -live=300
```

This places 30 orders over 5 minutes (~10s between each order).

## Status Distribution (Batch Mode)

- **Open** — Only in the last 5% of the date range, with increasing probability toward today (~50% average in that zone)
- **Completed** — ~65% of regular orders (payment paid, delivery shipped)
- **In Progress** — ~35% of regular orders (payment paid)
- **Cancelled** — Half of the `-failed-orders` percentage
- **Refunded** — Other half of the `-failed-orders` percentage (payment paid + refunded)

## Project Structure

```
ShopwareDataHydrator/
├── Program.cs                          Entry point and CLI
├── Api/
│   ├── AdminApiClient.cs               Shopware Admin API client
│   └── StorefrontApiClient.cs          Shopware Storefront API client
├── Hydrators/
│   ├── CustomerHydrator.cs             Customer creation with Bogus
│   └── OrderHydrator.cs                Order logic and status management
└── Models/
    ├── AppConfig.cs
    ├── CountryInfo.cs
    ├── CustomerInfo.cs
    ├── EmailHelper.cs
    ├── OrderDetails.cs
    ├── ProductInfo.cs
    ├── SalesChannelInfo.cs
    ├── SalutationInfo.cs
    └── ShopData.cs
```

## Notes

- It is recommended to disable email sending in Shopware flows before running to avoid mass emails
- The tool creates orders using products available in the sales channel — active, purchasable products must exist
- Promotion codes are automatically detected and applied to ~20% of orders if available
