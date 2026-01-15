using Stunstick.Core.Mdl;
using System.Globalization;

namespace Stunstick.App.Decompile;

internal static class MdlFlexRuleFormatter
{
	// SourceEngine2006+ studio.h flex opcodes.
	private const int STUDIO_CONST = 1;
	private const int STUDIO_FETCH1 = 2;
	private const int STUDIO_FETCH2 = 3;
	private const int STUDIO_ADD = 4;
	private const int STUDIO_SUB = 5;
	private const int STUDIO_MUL = 6;
	private const int STUDIO_DIV = 7;
	private const int STUDIO_NEG = 8;
	private const int STUDIO_EXP = 9;
	private const int STUDIO_OPEN = 10;
	private const int STUDIO_CLOSE = 11;
	private const int STUDIO_COMMA = 12;
	private const int STUDIO_MAX = 13;
	private const int STUDIO_MIN = 14;
	private const int STUDIO_2WAY_0 = 15;
	private const int STUDIO_2WAY_1 = 16;
	private const int STUDIO_NWAY = 17;
	private const int STUDIO_COMBO = 18;
	private const int STUDIO_DOMINATE = 19;
	private const int STUDIO_DME_LOWER_EYELID = 20;
	private const int STUDIO_DME_UPPER_EYELID = 21;

	private sealed record IntermediateExpression(string Expression, int Precedence);

	public static string GetFlexRuleLine(
		IReadOnlyList<MdlFlexDesc> flexDescs,
		IReadOnlyList<MdlFlexController> flexControllers,
		MdlFlexRule flexRule,
		string indent)
	{
		var flexName = (uint)flexRule.FlexIndex < (uint)flexDescs.Count
			? flexDescs[flexRule.FlexIndex].Name
			: string.Empty;
		if (string.IsNullOrWhiteSpace(flexName))
		{
			flexName = $"flex_{flexRule.FlexIndex}";
		}

		var linePrefix = $"{indent}%{flexName} = ";

		if (flexRule.Ops is null || flexRule.Ops.Count == 0)
		{
			return indent + "// [Empty flex rule found and ignored.]";
		}

		var stack = new Stack<IntermediateExpression>();
		var dmxFlexOpWasUsed = false;

		for (var i = 0; i < flexRule.Ops.Count; i++)
		{
			var op = flexRule.Ops[i];

			if (op.Op == STUDIO_CONST)
			{
				stack.Push(new IntermediateExpression(
					Math.Round(op.Value, 6).ToString("0.######", CultureInfo.InvariantCulture),
					10));
				continue;
			}

			if (op.Op == STUDIO_FETCH1)
			{
				var name = (uint)op.Index < (uint)flexControllers.Count
					? flexControllers[op.Index].Name
					: string.Empty;
				if (string.IsNullOrWhiteSpace(name))
				{
					name = $"controller_{op.Index}";
				}
				stack.Push(new IntermediateExpression(name, 10));
				continue;
			}

			if (op.Op == STUDIO_FETCH2)
			{
				var name = (uint)op.Index < (uint)flexDescs.Count
					? flexDescs[op.Index].Name
					: string.Empty;
				if (string.IsNullOrWhiteSpace(name))
				{
					name = $"flex_{op.Index}";
				}
				stack.Push(new IntermediateExpression("%" + name, 10));
				continue;
			}

			if (op.Op == STUDIO_ADD)
			{
				if (stack.Count < 2) { stack.Clear(); break; }
				var right = stack.Pop();
				var left = stack.Pop();
				stack.Push(new IntermediateExpression(left.Expression + " + " + right.Expression, 1));
				continue;
			}

			if (op.Op == STUDIO_SUB)
			{
				// The model compiler sometimes stores STUDIO_SUB for a unary '-' at start-of-line.
				if (stack.Count >= 2)
				{
					var right = stack.Pop();
					var left = stack.Pop();
					stack.Push(new IntermediateExpression(left.Expression + " - " + right.Expression, 1));
				}
				else if (stack.Count == 1)
				{
					var right = stack.Pop();
					stack.Push(new IntermediateExpression("-(" + right.Expression + ")", 10));
				}
				else
				{
					stack.Clear();
					break;
				}
				continue;
			}

			if (op.Op == STUDIO_MUL)
			{
				if (stack.Count < 2) { stack.Clear(); break; }
				var right = stack.Pop();
				var left = stack.Pop();
				var rightExpr = right.Precedence < 2 ? "(" + right.Expression + ")" : right.Expression;
				var leftExpr = left.Precedence < 2 ? "(" + left.Expression + ")" : left.Expression;
				stack.Push(new IntermediateExpression(leftExpr + " * " + rightExpr, 2));
				continue;
			}

			if (op.Op == STUDIO_DIV)
			{
				if (stack.Count < 2) { stack.Clear(); break; }
				var right = stack.Pop();
				var left = stack.Pop();
				var rightExpr = right.Precedence < 2 ? "(" + right.Expression + ")" : right.Expression;
				var leftExpr = left.Precedence < 2 ? "(" + left.Expression + ")" : left.Expression;
				stack.Push(new IntermediateExpression(leftExpr + " / " + rightExpr, 2));
				continue;
			}

			if (op.Op == STUDIO_NEG)
			{
				if (stack.Count < 1) { stack.Clear(); break; }
				var right = stack.Pop();
				stack.Push(new IntermediateExpression("-(" + right.Expression + ")", 10));
				continue;
			}

			if (op.Op == STUDIO_MAX)
			{
				if (stack.Count < 2) { stack.Clear(); break; }
				var right = stack.Pop();
				var left = stack.Pop();
				var rightExpr = right.Precedence < 5 ? "(" + right.Expression + ")" : right.Expression;
				var leftExpr = left.Precedence < 5 ? "(" + left.Expression + ")" : left.Expression;
				stack.Push(new IntermediateExpression("max(" + leftExpr + ", " + rightExpr + ")", 5));
				continue;
			}

			if (op.Op == STUDIO_MIN)
			{
				if (stack.Count < 2) { stack.Clear(); break; }
				var right = stack.Pop();
				var left = stack.Pop();
				var rightExpr = right.Precedence < 5 ? "(" + right.Expression + ")" : right.Expression;
				var leftExpr = left.Precedence < 5 ? "(" + left.Expression + ")" : left.Expression;
				stack.Push(new IntermediateExpression("min(" + leftExpr + ", " + rightExpr + ")", 5));
				continue;
			}

			if (op.Op == STUDIO_2WAY_0)
			{
				var controllerName = (uint)op.Index < (uint)flexControllers.Count
					? flexControllers[op.Index].Name
					: $"controller_{op.Index}";
				var newExpression = "(1 - (min(max(" + controllerName + " + 1, 0), 1)))";
				stack.Push(new IntermediateExpression(newExpression, 5));
				dmxFlexOpWasUsed = true;
				continue;
			}

			if (op.Op == STUDIO_2WAY_1)
			{
				var controllerName = (uint)op.Index < (uint)flexControllers.Count
					? flexControllers[op.Index].Name
					: $"controller_{op.Index}";
				var newExpression = "(min(max(" + controllerName + ", 0), 1))";
				stack.Push(new IntermediateExpression(newExpression, 5));
				dmxFlexOpWasUsed = true;
				continue;
			}

			if (op.Op == STUDIO_NWAY)
			{
				if ((uint)op.Index >= (uint)flexControllers.Count || stack.Count < 5)
				{
					stack.Clear();
					break;
				}

				var v = flexControllers[op.Index];

				var valueControllerIndex = stack.Pop();
				var flValueIndex = int.TryParse(valueControllerIndex.Expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValueIndex)
					? parsedValueIndex
					: -1;
				var flValue = (uint)flValueIndex < (uint)flexControllers.Count ? flexControllers[flValueIndex].Name : $"controller_{flValueIndex}";

				var filterRampW = stack.Pop();
				var filterRampZ = stack.Pop();
				var filterRampY = stack.Pop();
				var filterRampX = stack.Pop();

				var greaterThanX = "min(1, (-min(0, (" + filterRampX.Expression + " - " + flValue + "))))";
				var lessThanY = "min(1, (-min(0, (" + flValue + " - " + filterRampY.Expression + "))))";
				var remapX = "min(max((" + flValue + " - " + filterRampX.Expression + ") / (" + filterRampY.Expression + " - " + filterRampX.Expression + "), 0), 1)";
				var greaterThanEqualY = "-(min(1, (-min(0, (" + flValue + " - " + filterRampY.Expression + ")))) - 1)";
				var lessThanEqualZ = "-(min(1, (-min(0, (" + filterRampZ.Expression + " - " + flValue + ")))) - 1)";
				var greaterThanZ = "min(1, (-min(0, (" + filterRampZ.Expression + " - " + flValue + "))))";
				var lessThanW = "min(1, (-min(0, (" + flValue + " - " + filterRampW.Expression + "))))";
				var remapZ = "(1 - (min(max((" + flValue + " - " + filterRampZ.Expression + ") / (" + filterRampW.Expression + " - " + filterRampZ.Expression + "), 0), 1)))";

				flValue = "((" + greaterThanX + " * " + lessThanY + ") * " + remapX + ") + (" + greaterThanEqualY + " * " + lessThanEqualZ + ") + ((" + greaterThanZ + " * " + lessThanW + ") * " + remapZ + ")";

				var newExpression = "((" + flValue + ") * (" + v.Name + "))";
				stack.Push(new IntermediateExpression(newExpression, 5));
				dmxFlexOpWasUsed = true;
				continue;
			}

			if (op.Op == STUDIO_COMBO)
			{
				var count = op.Index;
				if (count <= 0 || stack.Count < count)
				{
					stack.Clear();
					break;
				}

				var newExpression = stack.Pop().Expression;
				for (var j = 2; j <= count; j++)
				{
					newExpression += " * " + stack.Pop().Expression;
				}

				stack.Push(new IntermediateExpression("(" + newExpression + ")", 5));
				dmxFlexOpWasUsed = true;
				continue;
			}

			if (op.Op == STUDIO_DOMINATE)
			{
				var count = op.Index;
				if (count <= 0 || stack.Count < count + 1)
				{
					stack.Clear();
					break;
				}

				var productExpression = stack.Pop().Expression;
				for (var j = 2; j <= count; j++)
				{
					productExpression += " * " + stack.Pop().Expression;
				}

				var baseExpression = stack.Pop().Expression;
				var newExpression = "(" + baseExpression + " * (1 - " + productExpression + "))";

				stack.Push(new IntermediateExpression(newExpression, 5));
				dmxFlexOpWasUsed = true;
				continue;
			}

			if (op.Op == STUDIO_DME_LOWER_EYELID || op.Op == STUDIO_DME_UPPER_EYELID)
			{
				if ((uint)op.Index >= (uint)flexControllers.Count || stack.Count < 3)
				{
					stack.Clear();
					break;
				}

				var pCloseLidV = flexControllers[op.Index];
				var flCloseLidVMin = Math.Round(pCloseLidV.Min, 6).ToString("0.######", CultureInfo.InvariantCulture);
				var flCloseLidVMax = Math.Round(pCloseLidV.Max, 6).ToString("0.######", CultureInfo.InvariantCulture);
				var flCloseLidV = "(min(max((" + pCloseLidV.Name + " - " + flCloseLidVMin + ") / (" + flCloseLidVMax + " - " + flCloseLidVMin + "), 0), 1))";

				var closeLidIndexExpr = stack.Pop();
				var closeLidIndex = int.TryParse(closeLidIndexExpr.Expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCloseLidIndex)
					? parsedCloseLidIndex
					: -1;
				var pCloseLid = (uint)closeLidIndex < (uint)flexControllers.Count ? flexControllers[closeLidIndex] : null;
				if (pCloseLid is null)
				{
					stack.Clear();
					break;
				}

				var flCloseLidMin = Math.Round(pCloseLid.Min, 6).ToString("0.######", CultureInfo.InvariantCulture);
				var flCloseLidMax = Math.Round(pCloseLid.Max, 6).ToString("0.######", CultureInfo.InvariantCulture);
				var flCloseLid = "(min(max((" + pCloseLid.Name + " - " + flCloseLidMin + ") / (" + flCloseLidMax + " - " + flCloseLidMin + "), 0), 1))";

				_ = stack.Pop(); // blinkIndex unused

				var eyeUpDownIndexExpr = stack.Pop();
				var eyeUpDownIndex = int.TryParse(eyeUpDownIndexExpr.Expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEyeUpDownIndex)
					? parsedEyeUpDownIndex
					: -1;
				var pEyeUpDown = (uint)eyeUpDownIndex < (uint)flexControllers.Count ? flexControllers[eyeUpDownIndex] : null;
				if (pEyeUpDown is null)
				{
					stack.Clear();
					break;
				}

				var flEyeUpDownMin = Math.Round(pEyeUpDown.Min, 6).ToString("0.######", CultureInfo.InvariantCulture);
				var flEyeUpDownMax = Math.Round(pEyeUpDown.Max, 6).ToString("0.######", CultureInfo.InvariantCulture);
				var flEyeUpDown = "(-1 + 2 * (min(max((" + pEyeUpDown.Name + " - " + flEyeUpDownMin + ") / (" + flEyeUpDownMax + " - " + flEyeUpDownMin + "), 0), 1)))";

				var newExpression = op.Op == STUDIO_DME_LOWER_EYELID
					? "(min(1, (1 - " + flEyeUpDown + ")) * (1 - " + flCloseLidV + ") * " + flCloseLid + ")"
					: "(min(1, (1 + " + flEyeUpDown + ")) * " + flCloseLidV + " * " + flCloseLid + ")";

				stack.Push(new IntermediateExpression(newExpression, 5));
				dmxFlexOpWasUsed = true;
				continue;
			}

			// Unsupported/parse-only ops.
			if (op.Op is STUDIO_EXP or STUDIO_OPEN or STUDIO_CLOSE or STUDIO_COMMA)
			{
				stack.Clear();
				break;
			}

			stack.Clear();
			break;
		}

		if (stack.Count == 1)
		{
			var expr = stack.Peek().Expression;
			if (dmxFlexOpWasUsed)
			{
				expr += " // WARNING: Expression is an approximation of what can only be done via DMX file.";
			}

			return linePrefix + expr;
		}

		if (stack.Count == 0)
		{
			return indent + "// " + linePrefix + "// ERROR: Unknown flex operation.";
		}

		return indent + "// " + linePrefix + stack.Peek().Expression + " // ERROR: Unknown flex operation.";
	}
}

