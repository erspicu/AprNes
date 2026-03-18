# Mapper 71: Camerica

- Source: `mappers-0.80.txt`
- Mapper number: `71`

## Overview

This mapper is used on Camerica's unlicensed NES carts, including Firehawk and Linus Spacehead.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped, as far as is known.
- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.
- Many ROMs from these games are incorrectly defined as mapper #2. Marat has still not assigned an "official" .NES mapper number for this mapper.

## Full Register And Behavior Reference

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on Camerica's unlicensed NES carts, including  |
 | Firehawk and Linus Spacehead.                                      |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $BFFF +---------| UUUUUUUU                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Unknown                      |
                           +------------------------------------------+

 +---------------+         +------------------------------------------+
 | $C000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
