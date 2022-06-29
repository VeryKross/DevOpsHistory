# DevOpsHistory
This is a small utility class wrapped in a Console app that is able to pull the full revision history for a specified Azure DevOps Work Item and output that in a human readable text file that shows when every field has been updated and by who. Finding specific changes to specific fields can be easily accomplished by using search features in your favorite text editor (e.g. VS Code).

When running the DevOpsHistory application for the first time, it will ask the user for three(3) pieces of information:
<ol>
<li>Your Personal Access Token (PAT)</li>
<li>The ADO Org you want to access</li>
<li>The folder where the output files will be saved</li>
</ol>

For each of the above, you'll be given the option to save the setting so that you don't have to supply it on subsequent runs. For any of these values that you don't save, it will ask you to provide it next time. If, on the other hand, you do save everything and then later need to make a change (e.g. your PAT expires or you want to change folders), just execute the utility from Windows Terminal or a command window with "clear" on the command line (e.g. "DevOpsHistory clear"). This will clear all previously stored values and ask you to supply them again.

<b>Note:</b> The PAT you supply will need to have Read access to the ADO Org Work Items that you want to search. The output folder will be creted if it doesn't already exist.
