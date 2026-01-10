# Instructions

Your goal is to remove duplicate code.
- Go through the specified code files, or through the entire code base if none specified and check for similar code passages.
- Extract larger (about 5 lines of code or more) similar code into utility functions in a utility class in the "Game/Utilities" folder.
- If no fitting utility class exists, create a new one in the "Game/Utilities" folder.
- If possible and reasonable, make this newly created class `static`.
- Replace all code passages that could be simplified by existing utilties with said utilities.
- Report additional redundancies such as duplicate or redundant function definitions to the user, but don't fix them.