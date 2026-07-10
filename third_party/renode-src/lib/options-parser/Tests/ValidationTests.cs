using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Antmicro.OptionsParser.Tests
{
    [TestFixture]
    public class ValidationTests
    {
        [Test]
        public void ShouldThrowExceptionWhenRequiredParamterIsNotFound()
        {
            var args = new string[0];

            var parser = new OptionsParser(new ParserConfiguration { ThrowValidationException = true });
            var options = new OptionsWithRequiredParameter();

            try
            {
                parser.Parse(options, args);
                Assert.Fail("Expected exception");
            }
            catch (ValidationException)
            {
                // intentionally left blank
            }
        }

        [Test]
        public void ShouldSetDefaultValueWhenParameterIsNotFound()
        {
            var args = new string[0];

            var parser = new OptionsParser();
            var options = new OptionsWithParameterWithDefaultValue();

            parser.Parse(options, args);
            Assert.AreEqual(0, parser.ParsedOptions.Count());
            Assert.AreEqual(147, options.NumericParameter);
        }

        [Test]
        public void ShouldCallCustomValidator()
        {
            var args = new [] { "--stringValue=foo", "-n1" };

            var parser = new OptionsParser(new ParserConfiguration { ThrowValidationException = true });
            var options = new OptionsWithCustomValidator();

            try
            {
                parser.Parse(options, args);
                Assert.Fail("Expected exception");
            }
            catch (ValidationException)
            {
                Assert.AreEqual(1, options.NumericValue);
                Assert.AreEqual("foo", options.StringValue);
            }
        }

        [Test]
        public void ShouldThrowAnExceptionWhenDefaultValueIsOfAWrongType()
        {
         var args = new [] { "--arg" };

            var parser = new OptionsParser();
            var options = new OptionsWithDefault();

            try
            {
                parser.Parse(options, args);
                Assert.Fail("Expected exception");
            }
            catch(ArgumentException e)
            {
                Assert.IsTrue(e.Message.Contains("is of unexpected type"));
            }
        }

        [OneTimeSetUp]
        public void ConfigureOutput()
        {
            var sw = new StringWriter();
            System.Console.SetOut(sw);
            System.Console.SetError(sw);
        }

        private class OptionsWithCustomValidator : IValidatedOptions
        {
            public int NumericValue { get; set; }

            public string StringValue { get; set; }

            public bool Validate(out string error)
            {
                if(NumericValue == 1 && StringValue == "foo")
                {
                    error = "Field cannot have values: 1 and 'foo' at the same time";
                    return false;
                }

                error = null;
                return true;
            }
        }

        private class OptionsWithParameterWithDefaultValue
        {
            [DefaultValue(147)]
            public int NumericParameter { get; set; }
        }

        private class OptionsWithRequiredParameter
        {
            [Required]
            public bool RequiredParameter { get; set; }
        }

        private class OptionsWithDefault
        {
            [DefaultValue(0)]
            public string Arg { get; set; }
        }
    }
}

