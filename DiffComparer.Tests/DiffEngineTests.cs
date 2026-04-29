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
    }
}
