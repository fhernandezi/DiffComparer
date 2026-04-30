using DiffComparer.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiffComparer.Tests
{
    public class DiffEngineTests
    {
        [Fact]
        public void Compare_WhenTextsAreEqual_ReturnsAllEqual()
        {
            var engine = new DiffEngine();

            var left = new[] { "A", "B", "C" };
            var right = new[] { "A", "B", "C" };

            var result = engine.Compare(left, right);

            Assert.Equal(3, result.Count);
            Assert.All(result, x => Assert.Equal(LineStatus.Equal, x.Status));
        }

        [Fact]
        public void Compare_WhenLineIsAdded_ReturnsAdded()
        {
            var engine = new DiffEngine();

            var left = new[] { "A", "B", "C" };
            var right = new[] { "A", "X", "B", "C" };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x =>
                x.RightText == "X" &&
                x.Status == LineStatus.Added);
        }

        [Fact]
        public void Compare_WhenLineIsDeleted_ReturnsDeleted()
        {
            var engine = new DiffEngine();

            var left = new[] { "A", "X", "B", "C" };
            var right = new[] { "A", "B", "C" };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x =>
                x.LeftText == "X" &&
                x.Status == LineStatus.Deleted);
        }

        [Fact]
        public void Compare_WhenLineIsModified_ReturnsModified()
        {
            var engine = new DiffEngine();

            var left = new[] { "Nombre: Pedrito" };
            var right = new[] { "Nombre: Pedro" };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x =>
                x.Status == LineStatus.Modified &&
                x.LeftText == "Nombre: Pedrito" &&
                x.RightText == "Nombre: Pedro");
        }

        [Fact]
        public void Compare_WhenWhitespaceChangesOnly_ReturnsEqual()
        {
            var engine = new DiffEngine();

            var left = new[] { "A    B     C" };
            var right = new[] { "A B C" };

            var result = engine.Compare(left, right);

            Assert.Single(result);
            Assert.Equal(LineStatus.Equal, result[0].Status);
        }

        [Fact]
        public void Compare_WhenLargeBlockIsInserted_KeepsSurroundingLinesAligned()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
            "Inicio",
            "Cliente: Pedrito",
            "Total: 100",
            "Fin"
        };

            var right = new[]
            {
            "Inicio",
            "Linea nueva 1",
            "Linea nueva 2",
            "Linea nueva 3",
            "Cliente: Pedrito",
            "Total: 100",
            "Fin"
        };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x => x.LeftText == "Inicio" && x.RightText == "Inicio" && x.Status == LineStatus.Equal);
            Assert.Contains(result, x => x.RightText == "Linea nueva 1" && x.Status == LineStatus.Added);
            Assert.Contains(result, x => x.RightText == "Linea nueva 2" && x.Status == LineStatus.Added);
            Assert.Contains(result, x => x.RightText == "Linea nueva 3" && x.Status == LineStatus.Added);
            Assert.Contains(result, x => x.LeftText == "Cliente: Pedrito" && x.RightText == "Cliente: Pedrito" && x.Status == LineStatus.Equal);
            Assert.Contains(result, x => x.LeftText == "Fin" && x.RightText == "Fin" && x.Status == LineStatus.Equal);
        }

        [Fact]
        public void Similarity_WhenTextsAreEqual_Returns100()
        {
            var score = DiffEngine.Similarity("Hola mundo", "Hola mundo");

            Assert.Equal(100, score);
        }

        [Fact]
        public void Similarity_WhenTextsAreDifferent_ReturnsLowScore()
        {
            var score = DiffEngine.Similarity("ABC", "Texto completamente distinto");

            Assert.True(score < 60);
        }

        [Fact]
        public void WordDiff_WhenOneWordChanges_MarksChangedSegment()
        {
            var result = DiffEngine.WordDiff(
                "Nombre: Pedrito",
                "Nombre: Pedro");

            Assert.Contains(result.Left, x => x.IsChanged);
            Assert.Contains(result.Right, x => x.IsChanged);
        }

        [Fact]
        public void Compare_WhenLineMoved_DoesNotCrashAndKeepsCommonLines()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
                "A",
                "B",
                "C",
                "D"
            };

            var right = new[]
            {
                "A",
                "C",
                "B",
                "D"
            };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x => x.LeftText == "A" && x.RightText == "A" && x.Status == LineStatus.Equal);
            Assert.Contains(result, x => x.LeftText == "D" && x.RightText == "D" && x.Status == LineStatus.Equal);
        }

        [Fact]
        public void Compare_WhenRepeatedLinesExist_StillAlignsUniqueLines()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
                "Header",
                "Item",
                "Item",
                "Total: 100",
                "Footer"
            };

            var right = new[]
            {
                "Header",
                "Item",
                "Item modificado",
                "Total: 100",
                "Footer"
            };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x => x.LeftText == "Header" && x.RightText == "Header" && x.Status == LineStatus.Equal);
            Assert.Contains(result, x => x.LeftText == "Total: 100" && x.RightText == "Total: 100" && x.Status == LineStatus.Equal);
            Assert.Contains(result, x => x.LeftText == "Footer" && x.RightText == "Footer" && x.Status == LineStatus.Equal);
        }

        [Fact]
        public void Compare_WhenTabsAndSpacesDiffer_ReturnsEqual()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
                "public    class    Persona"
            };

            var right = new[]
            {
                "public\tclass Persona"
            };

            var result = engine.Compare(left, right);

            Assert.Single(result);
            Assert.Equal(LineStatus.Equal, result[0].Status);
        }

        [Fact]
        public void Compare_WhenEmptyLinesDiffer_IgnoresWhitespaceOnlyDifference()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
                "A",
                "",
                "B"
            };

            var right = new[]
            {
                "A",
                "   ",
                "B"
            };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x => x.LeftText == "A" && x.RightText == "A" && x.Status == LineStatus.Equal);
            Assert.Contains(result, x => x.LeftText == "B" && x.RightText == "B" && x.Status == LineStatus.Equal);
        }

        [Fact]
        public void Compare_WhenManyLinesAreAddedBetweenAnchors_KeepsAfterAnchorAligned()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
                "using System;",
                "namespace Demo",
                "{",
                "    public class Persona",
                "    {",
                "        public string Nombre { get; set; }",
                "    }",
                "}"
            };

                    var right = new[]
                    {
                "using System;",
                "namespace Demo",
                "{",
                "    public class Persona",
                "    {",
                "        public int Id { get; set; }",
                "        public string Codigo { get; set; }",
                "        public DateTime FechaCreacion { get; set; }",
                "        public string Nombre { get; set; }",
                "    }",
                "}"
            };

            var result = engine.Compare(left, right);

            Assert.Contains(result, x => x.RightText.Contains("public int Id") && x.Status == LineStatus.Added);
            Assert.Contains(result, x => x.RightText.Contains("public string Codigo") && x.Status == LineStatus.Added);
            Assert.Contains(result, x => x.RightText.Contains("public DateTime FechaCreacion") && x.Status == LineStatus.Added);

            Assert.Contains(result, x =>
                x.LeftText.Trim() == "public string Nombre { get; set; }" &&
                x.RightText.Trim() == "public string Nombre { get; set; }" &&
                x.Status == LineStatus.Equal);
        }

        [Fact]
        public void WordDiff_WhenOnlyNumberChanges_MarksNumberAsChanged()
        {
            var result = DiffEngine.WordDiff(
                "Total: 100",
                "Total: 120");

            Assert.Contains(result.Left, x => x.Text == "100" && x.IsChanged);
            Assert.Contains(result.Right, x => x.Text == "120" && x.IsChanged);
        }

        [Fact]
        public void WordDiff_WhenPunctuationChanges_MarksSymbolAsChanged()
        {
            var result = DiffEngine.WordDiff(
                "Hola mundo.",
                "Hola mundo!");

            Assert.Contains(result.Left, x => x.Text == "." && x.IsChanged);
            Assert.Contains(result.Right, x => x.Text == "!" && x.IsChanged);
        }

        [Fact]
        public void Compare_WhenOnlyOneSideIsEmpty_ReturnsAllAdded()
        {
            var engine = new DiffEngine();

            var left = Array.Empty<string>();
            var right = new[]
            {
        "A",
        "B",
        "C"
    };

            var result = engine.Compare(left, right);

            Assert.Equal(3, result.Count);
            Assert.All(result, x => Assert.Equal(LineStatus.Added, x.Status));
        }

        [Fact]
        public void Compare_WhenRightSideIsEmpty_ReturnsAllDeleted()
        {
            var engine = new DiffEngine();

            var left = new[]
            {
        "A",
        "B",
        "C"
    };

            var right = Array.Empty<string>();

            var result = engine.Compare(left, right);

            Assert.Equal(3, result.Count);
            Assert.All(result, x => Assert.Equal(LineStatus.Deleted, x.Status));
        }
    }
}
