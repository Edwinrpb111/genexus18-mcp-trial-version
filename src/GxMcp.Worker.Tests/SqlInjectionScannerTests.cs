using GxMcp.Worker.Helpers;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class SqlInjectionScannerTests
    {
        [Fact]
        public void ConcatMethodInWhere_Flagged()
        {
            string src = @"For each Aluno
    Where AluNome = &filter.Concat('%')
endfor";
            var hits = SqlInjectionScanner.Scan(src);
            Assert.Single(hits);
            Assert.Equal(2, hits[0].Line);
            Assert.Contains("parameterized", hits[0].Suggestion);
        }

        [Fact]
        public void StringConcatInWhere_Flagged()
        {
            string src = @"For each Aluno
    Where AluNome = ""'"" + &filter + ""'""
endfor";
            var hits = SqlInjectionScanner.Scan(src);
            Assert.Single(hits);
            Assert.Equal(2, hits[0].Line);
        }

        [Fact]
        public void SafeParameterizedWhere_NotFlagged()
        {
            string src = @"For each Aluno
    Where AluCod = &AluCod
endfor";
            var hits = SqlInjectionScanner.Scan(src);
            Assert.Empty(hits);
        }

        [Fact]
        public void SafeLikeWithParam_NotFlagged()
        {
            string src = @"For each Aluno
    Where AluNome like &pattern
endfor";
            var hits = SqlInjectionScanner.Scan(src);
            Assert.Empty(hits);
        }

        [Fact]
        public void DynamicSqlBuilding_Flagged()
        {
            string src = @"&query = ""select * from t where col = "" + &user
&dyn = ""where col2 = "" + &v";
            var hits = SqlInjectionScanner.Scan(src);
            // both lines match dynamic-sql build
            Assert.Equal(2, hits.Count);
        }

        [Fact]
        public void CommentLine_NotFlagged()
        {
            string src = @"For each Aluno
    // Where AluNome = &filter.Concat('%')
    Where AluCod = &AluCod
endfor";
            var hits = SqlInjectionScanner.Scan(src);
            Assert.Empty(hits);
        }

        [Fact]
        public void EmptyOrNull_ReturnsEmpty()
        {
            Assert.Empty(SqlInjectionScanner.Scan(null));
            Assert.Empty(SqlInjectionScanner.Scan(""));
            Assert.Empty(SqlInjectionScanner.Scan("   \r\n  "));
        }
    }
}
