
## The LMM Parse Project
Help me plan an app that will parse a PDF and save portions of if into MarkDown
- The PDF is an agenda for our congregation (LivingMessiah.com) that is created every Saturday
- It is uploaded in an Azure Blob Storage located here `https://livingmessiahstorage.blob.core.windows.net/shabbat-service/`
- the file name format of the PDF name starts with the date (YYYY-MM-DD) then a hyphen, then a citation in the Torah e.g. `2026-07-04-Lev-16.pdf` here's another one `	2026-06-06-Lev-12-1-to-13-28.pdf`
- I'm only interested in the part that starts after the "Welcome" and the next line under that is "Bienvenido".  The end of the desired content is before the page titled "The Avinu Prayer".
- Most of that content is Bible verses with some supporting commentary and a few images
- I want that content to be saved in another folder `https://livingmessiahstorage.blob.core.windows.net/shabbat-service-md/` with the same name except ending in `.md` instead of .`pdf`

## About Me
I want the project saved in GitHub under my Github account `https://github.com/JohnMarsing`

I'm a c# developer with excellent knowledge in building Blazor Web Apps, Azure, and Sql Server. Also very good with Console and Azure Functions (to some degree). 

I'm open to options to various solutions but in the end I want a project I can understand

I'm new to Grok Build as this is my first solution using this tool, so help along the way would be useful

# Prompt 2
- [PdfPig](https://github.com/UglyToad/PdfPig)
- In the .md files, remember the pages of the PDF that the content came from