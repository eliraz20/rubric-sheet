# Rubric Sheet

Reuse versioned rubrics and return individual, criterion-level feedback for revision.

## Try it

From this folder with the .NET 10 SDK installed:

```sh
dotnet run -- grade course.db study.tsv
```

Rows: rule / version / criterion / weight / level / earned percent / descriptor; submission / submission ID / student / version; score / submission ID / criterion / level / revision feedback. Once a version has a submission its rules are frozen. Use a new version for new work. Redirect output to export feedback, or append a submission ID to export only that submission.

The included study.tsv uses tab-separated fields. Comment lines start with `#`.

The database is created on first use. Use a different database filename for another class or team.

The original exercise remains available with `dotnet run -- example.tsv`.

## When to use it

A TA or peer assessor needs to grade a roster consistently and send each student a revision note.

Example result:

```text
Alex / S1 / rubric v1
  Clarity — Developing: Argument needs signposts. Revision: Add a topic sentence
  Evidence — Strong: Claims supported. Revision: Keep the citation
  Weighted score 83.33%
```

Related course or field workflow: [McGill peer assessment guidance](https://teachingkb.mcgill.ca/tlk/peer-assessment-of-other-students-assignments).
