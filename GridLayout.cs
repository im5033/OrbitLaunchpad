using System;
using System.Windows;

namespace Orbit;

internal readonly record struct GridLayout(double Left, double Top, double CellWidth, double CellHeight, double IconSize)
{
    public Point TilePosition(int index, int columns, double tileWidth) => new(Left + CellWidth * (index % columns + .5) - tileWidth / 2, Top + CellHeight * (index / columns));
    public int IndexAt(Point point, int columns, int page, int capacity) => page * capacity + (int)Math.Floor((point.Y - Top) / CellHeight) * columns + (int)Math.Floor((point.X - Left) / CellWidth);
    public bool Contains(Point point, int columns, int rows) => point.X >= Left && point.X < Left + CellWidth * columns && point.Y >= Top && point.Y < Top + CellHeight * rows;
    public static GridLayout Reference(double width, double height, Preferences preferences)
    {
        double scale = Math.Min(width / 2048, height / 1152);
        double cellWidth = width * .8203125 / preferences.Columns;
        double cellHeight = height * .6875 / preferences.Rows;
        double size = Math.Clamp(Math.Min(preferences.IconSize * scale, Math.Min(cellWidth * .66, cellHeight * .74)), 36, 180);
        return new GridLayout(width * .08984375, height / 19.2, cellWidth, cellHeight, size);
    }
}
