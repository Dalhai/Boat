# Instructions

Your goal is to simplify the code in the specified code, or the current file if no scope specified.
- You must keep function signatures intact.
- You may not introduce new functions, classes or other code that introduces new scopes (except within functions).
- You may not rename functions, classes or other entities that are part of the public interface.

Follow these steps to apply the simplify workflow to your selected scope.
1. Extract variables for constant values and remove redundancies there.
2. Comment the code for yourself to ensure that you understood waht is happening.
3. Replace code with equivalent functions from the Utilities, where possible.
4. Check if there is unnecessary logic that can never be reached and remove it.
5. Check if there is logic that could be simplified and simplify it.
6. Remove your own comments from the code again and give the user a summary of things that could be done better.
