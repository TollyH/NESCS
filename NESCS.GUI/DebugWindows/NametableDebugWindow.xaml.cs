using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NESCS.GUI.DebugWindows
{
    /// <summary>
    /// Interaction logic for NametableDebugWindow.xaml
    /// </summary>
    public partial class NametableDebugWindow : Window
    {
        public const int DisplayWidth = PPU.VisibleCyclesPerScanline * 2;
        public const int DisplayHeight = PPU.VisibleScanlinesPerFrame * 2;

        private const int nametableRowSize = PPU.VisibleCyclesPerScanline / 8;
        private const int nametableSize = nametableRowSize * PPU.VisibleScanlinesPerFrame / 8;
        private const int attributeTableRowSize = PPU.VisibleCyclesPerScanline / 32;
        private const int attributeTableSize = attributeTableRowSize * 8;
        private const int totalTableSize = nametableSize + attributeTableSize;

        private readonly WriteableBitmap nametableBitmap = new(
            DisplayWidth, DisplayHeight, 96, 96, PixelFormats.Rgb24, null);

        public NametableDebugWindow()
        {
            InitializeComponent();

            displayContainer.Width = DisplayWidth;
            displayContainer.Height = DisplayHeight;
            nametableDisplay.Width = DisplayWidth;
            nametableDisplay.Height = DisplayHeight;
            nametableDisplay.Source = nametableBitmap;
        }

        public void UpdateDisplay(NESSystem nesSystem)
        {
            bool rightNametables = (nesSystem.PpuCore.Registers.T & 0b10000000000) != 0;
            bool bottomNametables = (nesSystem.PpuCore.Registers.T & 0b100000000000) != 0;

            int scrollX = (nesSystem.PpuCore.Registers.T & 0b11111) * 8 + nesSystem.PpuCore.Registers.X;
            int scrollY = ((nesSystem.PpuCore.Registers.T & 0b1111100000) >> 5) * 8 + ((nesSystem.PpuCore.Registers.T & 0b111000000000000) >> 12);

            int nametableX = rightNametables ? PPU.VisibleCyclesPerScanline : 0;
            int nametableY = bottomNametables ? PPU.VisibleScanlinesPerFrame : 0;

            scrollOverlay.Margin = new Thickness(scrollX + nametableX, scrollY + nametableY, 0, 0);

            // If scroll region has partially wrapped around the screen, multiple rectangles are used to show the wrapped around portion(s)
            if (rightNametables)
            {
                scrollOverlayWraparoundHorizontal.Visibility = Visibility.Visible;
                scrollOverlayWraparoundHorizontal.Margin = new Thickness(scrollX - nametableX, scrollY + nametableY, 0, 0);
            }
            else
            {
                scrollOverlayWraparoundHorizontal.Visibility = Visibility.Collapsed;
            }

            if (bottomNametables)
            {
                scrollOverlayWraparoundVertical.Visibility = Visibility.Visible;
                scrollOverlayWraparoundVertical.Margin = new Thickness(scrollX + nametableX, scrollY - nametableY, 0, 0);
            }
            else
            {
                scrollOverlayWraparoundVertical.Visibility = Visibility.Collapsed;
            }

            if (bottomNametables && rightNametables)
            {
                scrollOverlayWraparoundBoth.Visibility = Visibility.Visible;
                scrollOverlayWraparoundBoth.Margin = new Thickness(scrollX - nametableX, scrollY - nametableY, 0, 0);
            }
            else
            {
                scrollOverlayWraparoundBoth.Visibility = Visibility.Collapsed;
            }

            DrawNametables(nesSystem.PpuCore);
        }

        private unsafe void DrawNametables(PPU ppu)
        {
            nametableBitmap.Lock();

            Color* backBuffer = (Color*)nametableBitmap.BackBuffer;

            for (int nametable = 0; nametable < 4; nametable++)
            {
                int startOffset = totalTableSize * nametable;
                for (int i = 0; i < PPU.VisibleCyclesPerScanline * PPU.VisibleScanlinesPerFrame; i++)
                {
                    int yPos = i / PPU.VisibleCyclesPerScanline;
                    int xPos = i % PPU.VisibleCyclesPerScanline;

                    byte nametableByte = ppu[(ushort)(PPU.NametableStartAddress + startOffset + (yPos / 8 * nametableRowSize) + (xPos / 8))];
                    byte attributeByte = ppu[(ushort)(PPU.AttributeTableStartAddress + startOffset + (yPos / 32 * attributeTableRowSize) + (xPos / 32))];

                    ushort patternAddress = (ushort)((nametableByte << 4) | (yPos & 0b111));
                    if ((ppu.Registers.PPUCTRL & PPUCTRLFlags.BackgroundTileSelect) != 0)
                    {
                        patternAddress |= 0b1000000000000;
                    }

                    byte patternTableHigh = ppu[(ushort)(patternAddress | 0b1000)];
                    byte patternTableLow = ppu[patternAddress];

                    // Left most (first) pixel is stored in most significant (last) bit
                    int bgXOffset = ~xPos & 0b111;
                    int bgBit = 1 << bgXOffset;
                    int bgPaletteIndex = ((patternTableLow & bgBit) >> bgXOffset)
                        | (((patternTableHigh & bgBit) >> bgXOffset) << 1);

                    // Get the current 16x16 quadrant from the 32x32 attribute data
                    // (each 4x4 tile area is packed in the same attribute byte, split into 2x2 tile areas that can be individually modified)
                    int bgPalette = (attributeByte >> (((xPos & 0b10000) >> 3) | (((yPos / 8) & 0b10) << 1))) & 0b11;

                    int paletteIndex = ppu[(ushort)(PPU.PaletteRAMStartAddress + ((bgPalette << 2) | bgPaletteIndex))];

                    int pixelIndex = yPos * DisplayWidth + xPos;
                    if (nametable % 2 != 0)
                    {
                        // Left nametables
                        pixelIndex += PPU.VisibleCyclesPerScanline;
                    }
                    if (nametable >= 2)
                    {
                        // Bottom nametables
                        pixelIndex += PPU.VisibleScanlinesPerFrame * DisplayWidth;
                    }

                    backBuffer[pixelIndex] = ppu.CurrentPalette[paletteIndex];
                }
            }

            // Refresh entire bitmap
            nametableBitmap.AddDirtyRect(new Int32Rect(0, 0, nametableBitmap.PixelWidth, nametableBitmap.PixelHeight));

            nametableBitmap.Unlock();
        }
    }
}
