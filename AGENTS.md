# Agent Instructions
You must respect established designs, requirements and functional instructions of all markdown files in the "doc/" folder.
The markdown files in the "doc/" folder contain architectural, design and functional specification for specific aspects of the game.

You must first devise a plan, before changing any files, and present that plan to the user.
You must iterate on the plan with the user, until the user tells you to implement the plan.
This plan should be highly detailed with individual steps clearly listed in linear order.
- Create a list and number the individual step that lead to the plan
- For each step also write a quick description of what must be done for that step
- For each step also add a section that describes problems you must be aware of
- The plan should at the end list design and architecture impacts.
- For each design or architecture impact, you must list which file in "Doc/" needs to be updated.

IMPORTANT! You *must* wait for the users acknowledgement of any plan, before starting implementation.
The exception to this rule is, if you need to take action to update your plan, do so.
When the user tells you to implement the plan, you can start making changes to the codebase.
- You must first do your implementation.
- You must, when you are done implementing, cross check the changes you made with the design and architecture impacts identified earlier.
- If the impacts need to be updated, do so now. Try to leave them as is though unless they are clearly incorrect.
- You must then ask the user if you are allowed to update the markdown files. Only do so if the user confirms.

## Project Structure

This is a Godot 4.5.1 project.
You can find documentation with examples here: https://docs.godotengine.org/en/stable/
You can find a full class reference here: https://docs.godotengine.org/en/stable/classes/index.html

If you are unsure about aspects of Godot, you must use the browser to check the documentation.