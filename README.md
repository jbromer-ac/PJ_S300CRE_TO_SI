# Sage 300 CRE to Sage Intacct Migration Tool

## Overview

A tool for migrating client data from Sage 300 CRE to Sage Intacct. The tool extracts data from existing Sage 300 CRE databases, applies customizable selection and translation logic to prepare the data, and delivers it into Sage Intacct via importable spreadsheet or API.

## Features

- **Data Extraction** — Pulls data directly from Sage 300 CRE databases
- **Customizable Selection Logic** — Filter and scope which records are included in the migration
- **Customizable Translation Logic** — Map and transform Sage 300 CRE data structures to Sage Intacct formats
- **Flexible Output** — Deliver migrated data via:
  - Importable spreadsheet (CSV/Excel)
  - Sage Intacct API

## Workflow

1. Connect to the source Sage 300 CRE database
2. Select records using configurable selection criteria
3. Apply translation/mapping logic to transform data for Sage Intacct
4. Output to spreadsheet or push directly via Sage Intacct API

## Project Structure

```
PJ_S300CRE_TO_SI/
├── README.md
```

## Getting Started

_Setup and usage instructions will be added as the project develops._

## Notes

- Each client migration may require custom selection and translation configurations
- Spreadsheet output is intended for use with Sage Intacct's standard import templates
- API output targets the Sage Intacct XML/REST API
