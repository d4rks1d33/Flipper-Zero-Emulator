using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Antmicro.OptionsParser.Tests
{
    [TestFixture]
    public class ParserTests
    {
        [Test]
        public void ShouldDetectShortSwitch()
        {
            var args = new [] { "-c" };
            var parser = new OptionsParser();
            parser.WithOption<bool>('c');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('c', parser.ParsedOptions.First().Flag.ShortName);
        }
        
        [Test]
        public void ShouldSetShortSwitchInClass()
        {
            var args = new [] { "-s" };
            var options = new OptionsWithBool();
            var parser = new OptionsParser();

            parser.Parse(options, args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual(options.Switch, true);
        }

        [Test]
        public void ShouldDetectLongSwitch()
        {
            var args = new [] { "--long" };
            var parser = new OptionsParser();
            parser.WithOption<bool>("long");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("long", parser.ParsedOptions.First().Flag.LongName);
        }

        [Test]
        public void ShouldDetectOptionHavingBothShortAndLongName()
        {
            var argsShort = new [] { "-l" };
            var argsLong = new [] { "--long" };

            foreach(var args in new [] { argsShort, argsLong })
            {
                var parser = new OptionsParser();
                parser.WithOption<bool>('l', "long");

                parser.Parse(args);

                Assert.AreEqual(1, parser.ParsedOptions.Count());
                var flag = parser.ParsedOptions.First().Flag;
                Assert.AreEqual('l', flag.ShortName);
                Assert.AreEqual("long", flag.LongName);
            }
        }

        [Test]
        public void ShouldDetectUnexpectedShortSwitch()
        {
            var args = new [] { "-xc" };
            var parser = new OptionsParser();
            parser.WithOption<bool>('c');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('c', parser.ParsedOptions.First().Flag.ShortName);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("x", parser.UnexpectedArguments.First().Value);
        }

        [Test]
        public void ShouldDetectUnexpectedLongSwitch()
        {
            var args = new [] { "--long", "--secondLong" };
            var parser = new OptionsParser();
            parser.WithOption<bool>("long");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("long", parser.ParsedOptions.First().Flag.LongName);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("secondLong", parser.UnexpectedArguments.First().Value);
        }

        [Test]
        public void ShouldDetectedUnexpectedShortSwitchWithValue()
        {
            var args = new [] { "-cx", "value with whitespace" };
            var parser = new OptionsParser();
            parser.WithOption<bool>('c');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('c', parser.ParsedOptions.First().Flag.ShortName);

            Assert.AreEqual(2, parser.UnexpectedArguments.Count());
            Assert.AreEqual("x", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("value with whitespace", parser.UnexpectedArguments.ElementAt(1).Value);
        }
        
        [Test]
        public void ShouldParseOptionValueWithHyphen()
        {
            var args = new [] { "-c", "value-with-hyphen" };
            var parser = new OptionsParser();
            parser.WithOption<string>('c');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('c', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual("value-with-hyphen", parser.ParsedOptions.First().Value);

            Assert.AreEqual(0, parser.UnexpectedArguments.Count());
        }

        [Test]
        public void ShouldThrowAnExceptionForShortOptionRequiringValueWhenThereIsNone()
        {
            var args = new [] { "-n" };
            var parser = new OptionsParser(new ParserConfiguration { ThrowValidationException = true });
            parser.WithOption<int>('n');

            try
            {
                parser.Parse(args);
                Assert.Fail("Should throw an exception");
            }
            catch (ValidationException e)
            {
                Assert.IsTrue(e.Message.Contains("requires parameter of type"));
            }
        }

        [Test]
        public void ShouldThrowAnExceptionForLongOptionRequiringValueWhenThereIsNone()
        {
            var args = new [] { "--long" };
            var parser = new OptionsParser(new ParserConfiguration { ThrowValidationException = true });
            parser.WithOption<int>("long");

            try
            {
                parser.Parse(args);
                Assert.Fail("Should throw an exception");
            }
            catch (ValidationException e)
            {
                Assert.IsTrue(e.Message.Contains("requires parameter of type"));
            }
        }

        [Test]
        public void ShouldParseShortSwitchWithValue()
        {
            var args = new [] { "-n123" };
            var parser = new OptionsParser();
            parser.WithOption<int>('n');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);
        }

        [Test]
        public void ShouldParseLongSwitchWithValue()
        {
            var args = new [] { "--number", "123" };
            var parser = new OptionsParser();
            parser.WithOption<int>("number");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("number", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);
        }

        [Test]
        public void ShouldParseLongSwitchWithStringWithAssignmentOperator()
        {
            var args = new [] { "--long=test" };
            var parser = new OptionsParser();
            parser.WithOption<string>("long");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("long", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual("test", parser.ParsedOptions.First().Value);
        }

        [Test]
        public void ShouldParseShortSwitchWithStringValue()
        {
            var args = new [] { "-nmValue" };
            var parser = new OptionsParser();
            parser.WithOption<bool>('n');
            parser.WithOption<String>('m');

            parser.Parse(args);

            Assert.AreEqual(2, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual('m', parser.ParsedOptions.Skip(1).First().Flag.ShortName);
            Assert.AreEqual("Value", parser.ParsedOptions.Skip(1).First().Value);
        }

        [Test]
        public void ShouldParseOptionWithSpacesInValue()
        {
            var args = new [] { "--option=Value with spaces" };
            var parser = new OptionsParser();
            parser.WithOption<string>("option");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("option", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual("Value with spaces", parser.ParsedOptions.First().Value);
        }

        [Test]
        public void ShouldParseShortOptionWithSeparatedValue()
        {
            var args = new [] { "-n", "123" };
            var parser = new OptionsParser();
            parser.WithOption<int>('n');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);
        }

        [Test]
        public void ShouldRecreateUnparsedArguments()
        {
            var args = new [] {
                "--expected",
                "-x",
                "value",
                "-y1",
                "--another-expected",
                "-Aw",
                "-z'this was unexpected'"
            };
            var parser = new OptionsParser();
            parser.WithOption<bool>("expected");
            parser.WithOption<bool>("another-expected");
            parser.WithOption<bool>('A');

            parser.Parse(args);

            Assert.AreEqual(3, parser.ParsedOptions.Count());
            Assert.AreEqual("expected", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual("another-expected", parser.ParsedOptions.ElementAt(1).Flag.LongName);
            Assert.AreEqual(@"-x value -y1 -w ""-z'this was unexpected'""", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateMixedShortFlag()
        {
            var args = new [] { "-AwB" };
            var parser = new OptionsParser();
            parser.WithOption<bool>('A');
            parser.WithOption<bool>('B');

            parser.Parse(args);

            Assert.AreEqual(2, parser.ParsedOptions.Count());
            Assert.AreEqual('A', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual('B', parser.ParsedOptions.Last().Flag.ShortName);
            Assert.AreEqual("-w", parser.RecreateUnparsedArguments());
        }
        
        [Test]
        public void ShouldNotRecreatedParsedValue()
        {
            var args = new [] { "--switch", "value" };
            var parser = new OptionsParser();
            parser.WithOption<string>("switch");
            
            parser.Parse(args);
            
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual(args[1], parser.ParsedOptions.First().Value);
            Assert.IsEmpty(parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRemoveEmptyShortFlagPrefix()
        {
            var args = new [] { "-AB" };
            var parser = new OptionsParser();
            parser.WithOption<bool>('A');
            parser.WithOption<bool>('B');

            parser.Parse(args);

            Assert.AreEqual(2, parser.ParsedOptions.Count());
            Assert.AreEqual('A', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual('B', parser.ParsedOptions.Last().Flag.ShortName);
            Assert.AreEqual(string.Empty, parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldAcceptExpectedValues()
        {
            var args = new [] { "-a", "-b", "value-1", "-c", "value-2", "-d", "value-3" };
            var parser = new OptionsParser();
            parser.WithValue("val1");
            parser.WithValue("val2");
            parser.WithOption<bool>('a');
            parser.WithOption<bool>('b');
            parser.WithOption<bool>('c');
            parser.WithOption<bool>('d');

            parser.Parse(args);

            Assert.AreEqual(4, parser.ParsedOptions.Count());
            Assert.AreEqual('a', parser.ParsedOptions.ElementAt(0).Flag.ShortName);
            Assert.AreEqual('b', parser.ParsedOptions.ElementAt(1).Flag.ShortName);
            Assert.AreEqual('c', parser.ParsedOptions.ElementAt(2).Flag.ShortName);
            Assert.AreEqual('d', parser.ParsedOptions.ElementAt(3).Flag.ShortName);
            Assert.AreEqual(2, parser.Values.Count());
            Assert.AreEqual("value-1", parser.Values.ElementAt(0).Value);
            Assert.AreEqual("value-2", parser.Values.ElementAt(1).Value);
            Assert.AreEqual("value-3", parser.RecreateUnparsedArguments());
        }
        
        [Test]
        public void ShouldParsePositionalValues()
        {
            var args = new [] { "value", "value-2" };
            var parser = new OptionsParser();
            var options = new OptionsWithPositionalValues();
            parser.Parse(options, args);
            
            Assert.AreEqual(1, parser.Values.Count());
            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            
            Assert.AreEqual("value", options.Value);
            Assert.AreEqual("value-2", parser.UnexpectedArguments.First().Value);
        }
        
        [Test]
        public void ShoulNotParseOnMissingRequiredPositionalValues()
        {
            var args = new string [0];
            var parser = new OptionsParser();
            var options = new OptionsWithRequiredPositionalValues();
            
            Assert.AreEqual(false, parser.Parse(options, args));
        }
        
        [Test]
        public void ShouldParseOptionWithMultipleValues()
        {
            var args = new [] { "-v", "1:2:3" };
            var parser = new OptionsParser();
            var options = new OptionsWithMultipleValues();
            parser.Parse(options, args);
            
            Assert.AreEqual(3, options.Values.Length);
            Assert.AreEqual(1, options.Values[0]);
            Assert.AreEqual(2, options.Values[1]);
            Assert.AreEqual(3, options.Values[2]);
            
            Assert.AreEqual(0, parser.Values.Count());
            Assert.AreEqual(0, parser.UnexpectedArguments.Count());
        }
        
        [Test]
        public void ShouldParseOptionWithMultipleValuesWithDefaultDelimiterValue()
        {
            var args = new [] { "-m", "this;is;a;test" };
            var parser = new OptionsParser();
            var options = new OptionsWithMultipleValuesWithDefaultDelimiterValue();
            parser.Parse(options, args);
            
            Assert.AreEqual(4, options.Values.Length);
            Assert.AreEqual("this", options.Values[0]);
            Assert.AreEqual("is", options.Values[1]);
            Assert.AreEqual("a", options.Values[2]);
            Assert.AreEqual("test", options.Values[3]);
            
            Assert.AreEqual(0, parser.Values.Count());
            Assert.AreEqual(0, parser.UnexpectedArguments.Count());
        }
        
        [Test]
        public void ShouldParseEnum()
        {
            var args = new [] { "-v", "Y" };
            var parser = new OptionsParser();
            var options = new OptionsWithEnum();
            parser.Parse(options, args);
            
            Assert.AreEqual(Enum.Y, options.Value);
        }
        
        [Test]
        public void ShouldParseEnumPositionalArgument()
        {
            var args = new [] { "Y" };
            var parser = new OptionsParser();
            var options = new OptionsWithPositionalEnumArgument();
            parser.Parse(options, args);
            
            Assert.AreEqual(Enum.Y, options.Value);
        }
        
        [Test]
        public void ShouldHandleIntOptionWithDefaultValueFollowedByString()
        {
            var args = new [] { "--value", "test" };
            var parser = new OptionsParser(new ParserConfiguration { AllowUnexpectedArguments = true });
            var options = new OptionsWithInt();
            parser.Parse(options, args);
            
            Assert.AreEqual(-1, options.Value);
            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("test", parser.UnexpectedArguments.First().Value);
            Assert.AreEqual("test", parser.RecreateUnparsedArguments());
        }
        
        [Test]
        public void ShouldNotParseAfterDoubleHyphen()
        {
            var args = new [] { "--", "--value", "test" };
            var parser = new OptionsParser(new ParserConfiguration { AllowUnexpectedArguments = true });
            var options = new OptionsWithInt();
            parser.Parse(options, args);
            
            Assert.AreEqual(-1, options.Value);
            Assert.AreEqual(0, parser.ParsedOptions.Count());
            Assert.AreEqual(2, parser.UnexpectedArguments.Count());
            Assert.AreEqual("--value", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("test", parser.UnexpectedArguments.ElementAt(1).Value);
            Assert.AreEqual("--value test", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldThrowAnExceptionOnUnexpectedMultipleOccurencesOfArgument()
        {
            var args = new[] { "--value", "5", "--value", "6" };
            var parser = new OptionsParser(new ParserConfiguration { ThrowValidationException = true });
            var options = new OptionsWithInt();
            
            Assert.Throws<ValidationException>(() =>
                parser.Parse(options, args));
        }

        [Test]
        public void ShouldHandleMultipleOccurencesOfArgument()
        {
            var args = new[] { "-v", "5", "-v", "6" };
            var parser = new OptionsParser();
            var options = new OptionsWithMultipleValues();
            parser.Parse(options, args);

            Assert.AreEqual(2, options.Values.Length);
            Assert.AreEqual(5, options.Values[0]);
            Assert.AreEqual(6, options.Values[1]);
        }

        [Test]
        public void ShouldRecreateUnparsedArgument1()
        {
            var args = new[] { "positional argument", "--wrong-arg", "-p8888" };
            var parser = new OptionsParser();
            parser.WithOption<int>('p');
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('p', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument2()
        {
            var args = new[] { "positional argument", "--wrong-arg", "-p", "8888" };
            var parser = new OptionsParser();
            parser.WithOption<int>('p');
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('p', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument3()
        {
            var args = new[] { "positional argument", "-p8888", "--wrong-arg" };
            var parser = new OptionsParser();
            parser.WithOption<int>('p');
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('p', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument4()
        {
            var args = new[] { "positional argument", "--wrong-arg", "--port", "8888" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.ToArray()[0].Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.ToArray()[0].Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument5()
        {
            var args = new[] { "positional argument", "--wrong-arg", "--port=8888" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.ToArray()[0].Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.ToArray()[0].Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument6()
        {
            var args = new[] { "positional argument", "--port", "8888", "--wrong-arg" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.ToArray()[0].Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.ToArray()[0].Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument7()
        {
            var args = new[] { "positional argument", "--port=8888", "--wrong-arg" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");
            var options = new OptionsWithMultipleArguments();
            parser.Parse(options, args);

            Assert.AreEqual("positional argument", options.Value);
            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.ToArray()[0].Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.ToArray()[0].Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument8()
        {
            var args = new[] { "--wrong-arg", "-n123" };
            var parser = new OptionsParser();
            parser.WithOption<int>('n');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument9()
        {
            var args = new[] { "--wrong-arg", "-n", "123" };
            var parser = new OptionsParser();
            parser.WithOption<int>('n');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument10()
        {
            var args = new[] { "--no-xwt", "--port", "8888" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("no-xwt", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--no-xwt", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument11()
        {
            var args = new[] { "--no-xwt", "--port=8888" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("no-xwt", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--no-xwt", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument12()
        {
            var args = new[] { "-n123", "--wrong-arg" };
            var parser = new OptionsParser();
            parser.WithOption<int>('n');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument13()
        {
            var args = new[] { "-n", "123", "--wrong-arg" };
            var parser = new OptionsParser();
            parser.WithOption<int>('n');

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual('n', parser.ParsedOptions.First().Flag.ShortName);
            Assert.AreEqual(123, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("wrong-arg", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--wrong-arg", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument14()
        {
            var args = new[] { "--port", "8888", "--no-xwt" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("no-xwt", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--no-xwt", parser.RecreateUnparsedArguments());
        }

        [Test]
        public void ShouldRecreateUnparsedArgument15()
        {
            var args = new[] { "--port=8888", "--no-xwt" };
            var parser = new OptionsParser();
            parser.WithOption<int>("port");

            parser.Parse(args);

            Assert.AreEqual(1, parser.ParsedOptions.Count());
            Assert.AreEqual("port", parser.ParsedOptions.First().Flag.LongName);
            Assert.AreEqual(8888, parser.ParsedOptions.First().Value);

            Assert.AreEqual(1, parser.UnexpectedArguments.Count());
            Assert.AreEqual("no-xwt", parser.UnexpectedArguments.ElementAt(0).Value);
            Assert.AreEqual("--no-xwt", parser.RecreateUnparsedArguments());
        }

        [OneTimeSetUp]
        public void ConfigureOutput()
        {
            var sw = new StringWriter();
            System.Console.SetOut(sw);
            System.Console.SetError(sw);
        }

        private class OptionsWithInt
        {
            [DefaultValue(-1)]
            public int Value { get; set; }
        }
        
        private class OptionsWithPositionalEnumArgument
        {
            [PositionalArgument(0)]
            public Enum Value { get; set; }
        }
        
        private class OptionsWithEnum
        {
            public Enum Value { get; set; }
        }
        
        private enum Enum
        {
            X,
            Y,
            Z
        }
        
        private class OptionsWithMultipleValues
        {
            [Name('v'), NumberOfElements(3), Delimiter(':')]
            public int[] Values { get; set; }
        }

        private class OptionsWithMultipleValuesWithDefaultDelimiterValue
        {
            [Name('m')]
            public string[] Values { get; set; }
        }
        
        private class OptionsWithBool
        {
            [Name('s'), DefaultValue(false)]
            public bool Switch { get; set; }
        }
        
        private class OptionsWithPositionalValues
        {
            [PositionalArgument(0), Name("value")]
            public string Value { get; set; }
        }
        
        private class OptionsWithRequiredPositionalValues
        {
            [Required, PositionalArgument(0), Name("value")]
            public string Value { get; set; }
        }

        private class OptionsWithMultipleArguments
        {
            [PositionalArgument(0), Name("value")]
            public string Value { get; set; }

            [PositionalArgument(1)]
            public string Arg2 { get; set; }

            [PositionalArgument(2)]
            public string Arg3 { get; set; }

            [PositionalArgument(3)]
            public string Arg4 { get; set; }
        }
    }
}

