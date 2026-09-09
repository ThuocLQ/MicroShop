CREATE TABLE IF NOT EXISTS CustomerSavedItems (
    CustomerId uuid NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    ProductId uuid NOT NULL,
    CreatedAtUtc timestamptz NOT NULL,
    PRIMARY KEY (CustomerId, ProductId)
);

CREATE INDEX IF NOT EXISTS IX_CustomerSavedItems_CustomerId_CreatedAtUtc
    ON CustomerSavedItems (CustomerId, CreatedAtUtc DESC);