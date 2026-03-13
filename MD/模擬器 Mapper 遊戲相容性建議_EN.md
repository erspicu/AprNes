1. The Mappers you have already implemented cover over 95% of the most popular NES titles, and in particular, the implementation of **Mapper 4 (MMC3)** and **Mapper 5 (MMC5)** demonstrates that your emulator is already capable of handling complex scanline interrupts and advanced expansion features.
2. 
3. However, if you want to pursue "grand slam" level game compatibility, you are currently missing the following highly representative Mappers:
4. 
5. ### **1\. Mapper 9 & 10 (MMC2 / MMC4) —— Representative title: *Punch-Out!!***
6. 
7. This is Nintendo's own advanced chip, featuring a very unique "graphics-detection auto-switch" mechanism.
8. 
9. * **Key game:** *Mike Tyson's Punch-Out!! / Punch-Out!!*  
10. * **Technical highlights:** When the PPU reads a specific Tile ID (e.g., $FD or $FE), the hardware automatically switches the CHR Bank. Without implementing these two Mappers, after entering the game you will find that character graphics are completely garbled.
11. 
12. ### **2\. Mapper 19 (Namco 163\) —— Representative titles: *Shin Megami Tensei II*, *Romance of the Three Kingdoms II***
13. 
14. This is a powerful chip developed by Namco, primarily active in Japanese Famicom games.
15. 
16. * **Key games:** *Shin Megami Tensei II*, *Romance of the Three Kingdoms II: Haou no Tairiku*, *Final Fantasy Gaiden: Seiken Densetsu*  
17. * **Technical highlights:** Supports an additional **8-channel wavetable synthesis audio** and internal RAM. This is essential if you want to play Japanese-style RPGs with great sound.
18. 
19. ### **3\. Mapper 21, 23, 25 (Konami VRC4) —— Representative titles: *TwinBee 3*, *Goemon 2***
20. 
21. Konami's own main chip, with many variants.
22. 
23. * **Key games:** *TwinBee 3*, *Ganbare Goemon 2*, *Ninja Gaiden* (related Japanese-licensed version)  
24. * **Technical highlights:** Similar to MMC3 but with a different interrupt mechanism, and special pin wiring that causes register addresses to shift slightly for different games.
25. 
26. ### **4\. Mapper 69 (Sunsoft FME-7 / 5B) —— Representative title: *Batman: Return of the Joker***
27. 
28. Sunsoft's masterpiece, known for smooth animation and high-quality audio.
29. 
30. * **Key games:** *Batman: Return of the Joker*, *Gimmick!*  
31. * **Technical highlights:** Features extremely precise CPU-cycle-based interrupts (Cycle-based IRQ), unlike MMC3's scanline counter.
32. 
33. ### ---
34. 
35. **Additional Recommendations: Current Coverage Status by Category**
36. 
37. | Category | Already Have | Recommended to Add |
38. | :---- | :---- | :---- |
39. | **Nintendo First-Party (MMC)** | 1, 2, 3, 4, 5, 6 | **9, 10** (for Punch-Out\!\!) |
40. | **Konami (VRC)** | (none) | **21, 23, 25** (for the VRC4 series) |
41. | **Namco** | (none) | **19** (for Japanese RPGs) |
42. | **Sunsoft** | (none) | **69** (for late-era titles) |
43. | **Early Simple Type** | 0, 11, 66, 71 | **99** (certain VS System arcade ports) |
44. 
45. **Next Step Recommendation:**
46. 
47. I recommend tackling **Mapper 9 (MMC2)** first, because *Punch-Out!!* is an extremely prestigious game in NES history, and its auto Bank-switching mechanism based on PPU tile reads is an excellent test of PPU emulation accuracy.
48. 
49. Would you like me to explain the **Mapper 9 latch switching logic** in detail?
