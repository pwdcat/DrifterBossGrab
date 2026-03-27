#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DrifterBossGrabMod.Balance
{
    // Uses shunting-yard algorithm
    // Supports operators, functions, constants, and variables
    public static class FormulaParser
    {
        // Cache for parsed formulas
        private static readonly Dictionary<string, List<Token>> _rpnCache = new();
        #region Token Types

        private enum TokenType
        {
            Number,
            Variable,
            Operator,
            Function,
            LeftParen,
            RightParen,
            Comma,
            UnaryMinus
        }

        private readonly struct Token
        {
            public readonly TokenType Type;
            public readonly string Value;
            public readonly double NumericValue;

            public Token(TokenType type, string value, double numericValue = 0)
            {
                Type = type;
                Value = value;
                NumericValue = numericValue;
            }

            public override string ToString() => $"{Type}({Value})";
        }

        #endregion

        #region Operator Definitions

        private static readonly Dictionary<string, (int Precedence, bool RightAssociative)> Operators = new()
        {
            ["+"] = (2, false),
            ["-"] = (2, false),
            ["*"] = (3, false),
            ["/"] = (3, false),
            ["%"] = (3, false),
            ["^"] = (4, true),
            ["~"] = (5, true), // Internal: unary minus
        };

        private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "floor", "ceil", "round", "abs", "sqrt", "log", "ln",
            "min", "max", "clamp", "sin", "cos", "tan", "sign", "pow"
        };

        private static readonly Dictionary<string, double> Constants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pi"] = Math.PI,
            ["e"] = Math.E,
            ["inf"] = double.PositiveInfinity,
            ["infinity"] = double.PositiveInfinity,
        };

        #endregion

        #region Public API

        // Evaluate a formula string and return the result as a float.
        public static float Evaluate(string formula, Dictionary<string, float> variables)
        {
            if (string.IsNullOrWhiteSpace(formula))
                return 0f;

            try
            {
                List<Token> rpn;
                
                // Fast path: use cached parsed formula if available
                if (!_rpnCache.TryGetValue(formula, out rpn))
                {
                    // Slow path: tokenize and shunt
                    var tokens = Tokenize(formula);
                    rpn = ShuntingYard(tokens);
                    _rpnCache[formula] = rpn;
                }

                double result = EvaluateRPN(rpn, variables);

                if (double.IsNaN(result))
                    return 0f;

                if (double.IsPositiveInfinity(result))
                    return float.MaxValue;

                if (double.IsNegativeInfinity(result))
                    return float.MinValue;

                return (float)result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[FormulaParser] Failed to evaluate formula '{formula}': {ex.Message}");
                return 0f;
            }
        }

        public static float Evaluate(string formula, RoR2.CharacterBody? body, Dictionary<string, float>? localVars = null)
        {
            var variables = FormulaRegistry.GetVariables(body, localVars);
            return Evaluate(formula, variables);
        }

        // Evaluate a formula string and return the result as an integer (auto-floored)
        public static int EvaluateInt(string formula, Dictionary<string, float> variables)
        {
            float result = Evaluate(formula, variables);

            if (float.IsPositiveInfinity(result) || result >= int.MaxValue)
                return int.MaxValue;

            if (float.IsNegativeInfinity(result) || result <= int.MinValue)
                return int.MinValue;

            return (int)Math.Floor(result);
        }

        public static int EvaluateInt(string formula, RoR2.CharacterBody? body, Dictionary<string, float>? localVars = null)
        {
            float result = Evaluate(formula, body, localVars);

            if (float.IsPositiveInfinity(result) || result >= int.MaxValue)
                return int.MaxValue;

            if (float.IsNegativeInfinity(result) || result <= int.MinValue)
                return int.MinValue;

            return (int)Math.Floor(result);
        }

        // Validate a formula string for syntax errors
        public static string? Validate(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula))
                return null; // Empty is valid (evaluates to 0)

            try
            {
                var tokens = Tokenize(formula);
                ShuntingYard(tokens);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static IEnumerable<string> GetAvailableVariableNames()
        {
            return FormulaRegistry.GetRegisteredVariableNames();
        }

        #endregion

        #region Tokenizer

        private static List<Token> Tokenize(string formula)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < formula.Length)
            {
                char c = formula[i];

                // Skip whitespace
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // Numbers (including decimals like .5 or 3.14)
                if (char.IsDigit(c) || (c == '.' && i + 1 < formula.Length && char.IsDigit(formula[i + 1])))
                {
                    var sb = new StringBuilder();
                    bool hasDot = false;
                    while (i < formula.Length && (char.IsDigit(formula[i]) || (formula[i] == '.' && !hasDot)))
                    {
                        if (formula[i] == '.') hasDot = true;
                        sb.Append(formula[i]);
                        i++;
                    }
                    string numStr = sb.ToString();
                    if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double numVal))
                        throw new FormatException($"Invalid number: '{numStr}'");
                    tokens.Add(new Token(TokenType.Number, numStr, numVal));
                    continue;
                }

                // Identifiers (variables, functions, constants)
                if (char.IsLetter(c) || c == '_')
                {
                    var sb = new StringBuilder();
                    while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_'))
                    {
                        sb.Append(formula[i]);
                        i++;
                    }
                    string identifier = sb.ToString();

                    // Check if it's a function (followed by '(')
                    int peek = i;
                    while (peek < formula.Length && char.IsWhiteSpace(formula[peek])) peek++;
                    if (peek < formula.Length && formula[peek] == '(' && Functions.Contains(identifier))
                    {
                        tokens.Add(new Token(TokenType.Function, identifier.ToLowerInvariant()));
                    }
                    else if (Constants.TryGetValue(identifier, out double constVal))
                    {
                        tokens.Add(new Token(TokenType.Number, identifier, constVal));
                    }
                    else
                    {
                        // It's a variable
                        tokens.Add(new Token(TokenType.Variable, identifier.ToUpperInvariant()));
                    }
                    continue;
                }

                // Operators and punctuation
                switch (c)
                {
                    case '+':
                        tokens.Add(new Token(TokenType.Operator, "+"));
                        i++;
                        break;
                    case '-':
                        // Determine if unary minus
                        if (IsUnaryMinus(tokens))
                        {
                            tokens.Add(new Token(TokenType.UnaryMinus, "~"));
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.Operator, "-"));
                        }
                        i++;
                        break;
                    case '*':
                        tokens.Add(new Token(TokenType.Operator, "*"));
                        i++;
                        break;
                    case '/':
                        tokens.Add(new Token(TokenType.Operator, "/"));
                        i++;
                        break;
                    case '%':
                        tokens.Add(new Token(TokenType.Operator, "%"));
                        i++;
                        break;
                    case '^':
                        tokens.Add(new Token(TokenType.Operator, "^"));
                        i++;
                        break;
                    case '(':
                        tokens.Add(new Token(TokenType.LeftParen, "("));
                        i++;
                        break;
                    case ')':
                        tokens.Add(new Token(TokenType.RightParen, ")"));
                        i++;
                        break;
                    case ',':
                        tokens.Add(new Token(TokenType.Comma, ","));
                        i++;
                        break;
                    default:
                        throw new FormatException($"Unexpected character: '{c}' at position {i}");
                }
            }

            return tokens;
        }

        private static bool IsUnaryMinus(List<Token> tokens)
        {
            if (tokens.Count == 0) return true;
            var last = tokens[tokens.Count - 1];
            return last.Type == TokenType.Operator
                || last.Type == TokenType.UnaryMinus
                || last.Type == TokenType.LeftParen
                || last.Type == TokenType.Comma
                || last.Type == TokenType.Function;
        }

        #endregion

        #region Shunting-Yard Algorithm

        private static List<Token> ShuntingYard(List<Token> tokens)
        {
            var output = new List<Token>();
            var operatorStack = new Stack<Token>();

            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Number:
                    case TokenType.Variable:
                        output.Add(token);
                        break;

                    case TokenType.Function:
                        operatorStack.Push(token);
                        break;

                    case TokenType.Comma:
                        while (operatorStack.Count > 0 && operatorStack.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(operatorStack.Pop());
                        }
                        if (operatorStack.Count == 0)
                            throw new FormatException("Misplaced comma or missing parenthesis");
                        break;

                    case TokenType.Operator:
                    case TokenType.UnaryMinus:
                    {
                        string op = token.Value;
                        var (prec, rightAssoc) = Operators[op];

                        while (operatorStack.Count > 0)
                        {
                            var top = operatorStack.Peek();
                            if (top.Type == TokenType.LeftParen) break;
                            if (top.Type == TokenType.Function)
                            {
                                output.Add(operatorStack.Pop());
                                continue;
                            }

                            if (Operators.TryGetValue(top.Value, out var topInfo))
                            {
                                if ((!rightAssoc && prec <= topInfo.Precedence) ||
                                    (rightAssoc && prec < topInfo.Precedence))
                                {
                                    output.Add(operatorStack.Pop());
                                    continue;
                                }
                            }
                            break;
                        }

                        operatorStack.Push(token);
                        break;
                    }

                    case TokenType.LeftParen:
                        operatorStack.Push(token);
                        break;

                    case TokenType.RightParen:
                        while (operatorStack.Count > 0 && operatorStack.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(operatorStack.Pop());
                        }
                        if (operatorStack.Count == 0)
                            throw new FormatException("Mismatched parentheses: missing '('");
                        operatorStack.Pop(); // Remove the '('

                        // If there's a function on top, pop it to output
                        if (operatorStack.Count > 0 && operatorStack.Peek().Type == TokenType.Function)
                        {
                            output.Add(operatorStack.Pop());
                        }
                        break;
                }
            }

            // Pop remaining operators
            while (operatorStack.Count > 0)
            {
                var top = operatorStack.Pop();
                if (top.Type == TokenType.LeftParen)
                    throw new FormatException("Mismatched parentheses: missing ')'");
                output.Add(top);
            }

            return output;
        }

        #endregion

        #region RPN Evaluator

        private static double EvaluateRPN(List<Token> rpn, Dictionary<string, float> variables)
        {
            var stack = new Stack<double>();

            foreach (var token in rpn)
            {
                switch (token.Type)
                {
                    case TokenType.Number:
                        stack.Push(token.NumericValue);
                        break;

                    case TokenType.Variable:
                    {
                        if (variables.TryGetValue(token.Value, out float value))
                        {
                            stack.Push(value);
                        }
                        else
                        {
                            throw new FormatException($"Unknown variable: '{token.Value}'");
                        }
                        break;
                    }

                    case TokenType.UnaryMinus:
                        if (stack.Count < 1)
                            throw new FormatException("Invalid expression: not enough operands for unary minus");
                        stack.Push(-stack.Pop());
                        break;

                    case TokenType.Operator:
                    {
                        if (stack.Count < 2)
                            throw new FormatException($"Invalid expression: not enough operands for operator '{token.Value}'");
                        double b = stack.Pop();
                        double a = stack.Pop();
                        stack.Push(ApplyOperator(token.Value, a, b));
                        break;
                    }

                    case TokenType.Function:
                        ApplyFunction(token.Value, stack);
                        break;

                    default:
                        throw new FormatException($"Unexpected token in RPN: {token}");
                }
            }

            if (stack.Count != 1)
                throw new FormatException($"Invalid expression: expected 1 result but got {stack.Count}");

            return stack.Pop();
        }

        private static double ApplyOperator(string op, double a, double b)
        {
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? (a >= 0 ? double.PositiveInfinity : double.NegativeInfinity) : a / b,
                "%" => b == 0 ? 0 : a % b,
                "^" => Math.Pow(a, b),
                _ => throw new FormatException($"Unknown operator: '{op}'")
            };
        }

        private static void ApplyFunction(string name, Stack<double> stack)
        {
            switch (name)
            {
                // Single-argument functions
                case "floor":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Floor(stack.Pop()));
                    break;
                case "ceil":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Ceiling(stack.Pop()));
                    break;
                case "round":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Round(stack.Pop()));
                    break;
                case "abs":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Abs(stack.Pop()));
                    break;
                case "sqrt":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Sqrt(stack.Pop()));
                    break;
                case "log":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Log10(stack.Pop()));
                    break;
                case "ln":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Log(stack.Pop()));
                    break;
                case "sin":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Sin(stack.Pop()));
                    break;
                case "cos":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Cos(stack.Pop()));
                    break;
                case "tan":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Tan(stack.Pop()));
                    break;
                case "sign":
                    RequireArgs(stack, 1, name);
                    stack.Push(Math.Sign(stack.Pop()));
                    break;

                // Two-argument functions
                case "min":
                    RequireArgs(stack, 2, name);
                    { double b = stack.Pop(); double a = stack.Pop(); stack.Push(Math.Min(a, b)); }
                    break;
                case "max":
                    RequireArgs(stack, 2, name);
                    { double b = stack.Pop(); double a = stack.Pop(); stack.Push(Math.Max(a, b)); }
                    break;
                case "pow":
                    RequireArgs(stack, 2, name);
                    { double b = stack.Pop(); double a = stack.Pop(); stack.Push(Math.Pow(a, b)); }
                    break;

                // Three-argument functions
                case "clamp":
                    RequireArgs(stack, 3, name);
                    { double hi = stack.Pop(); double lo = stack.Pop(); double val = stack.Pop(); stack.Push(Math.Clamp(val, lo, hi)); }
                    break;

                default:
                    throw new FormatException($"Unknown function: '{name}'");
            }
        }

        private static void RequireArgs(Stack<double> stack, int count, string funcName)
        {
            if (stack.Count < count)
                throw new FormatException($"Function '{funcName}' requires {count} argument(s) but got {stack.Count}");
        }

        #endregion
    }
}
