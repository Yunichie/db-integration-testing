CREATE TABLE customers (
    id    integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name  text    NOT NULL,
    city  text    NOT NULL,
    email text    NOT NULL UNIQUE                    -- rule 7
);

CREATE TABLE products (
    id       integer       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name     text          NOT NULL,
    category text          NOT NULL,
    price    numeric(12,2) NOT NULL CHECK (price >= 0),
    stock    integer       NOT NULL CHECK (stock >= 0)   -- rule 4
);

CREATE TABLE orders (
    id          integer     GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id integer     NOT NULL REFERENCES customers (id),
    status      text        NOT NULL DEFAULT 'Pending'
                CHECK (status IN ('Pending', 'Paid', 'Shipped', 'Cancelled')),  -- rule 3
    ordered_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE order_items (
    id         integer       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id   integer       NOT NULL REFERENCES orders (id),
    product_id integer       NOT NULL REFERENCES products (id),
    quantity   integer       NOT NULL CHECK (quantity > 0),
    unit_price numeric(12,2) NOT NULL CHECK (unit_price >= 0)   -- rule 2: price at purchase
);
