-- public.product definition

-- Drop table

-- DROP TABLE public.product;

CREATE TABLE public.product (
	id int8 NOT NULL,
	"name" text NOT NULL,
	barcode varchar NULL,
	generic_name text NOT NULL,
	"type" varchar(50) NOT NULL,
	origin varchar(50) NULL,
	category varchar NOT NULL,
	sub_category varchar(50) NULL,
	dose varchar NULL,
	image_name text NULL,
	url text NULL,
	image_uploaded_by_user_id int4 NULL,
	is_prescription_required bool NOT NULL,
	is_strip_allowed bool NOT NULL,
	medicine_per_strips int4 NOT NULL,
	cost_per_unit float8 NULL,
	vat float8 NULL,
	new_cost_per_unit float8 NULL,
	rate_per_unit float8 NULL,
	is_approved bool NOT NULL,
	is_active bool NOT NULL,
	is_deleted bool NOT NULL,
	deleted_by_crm_user_id int4 NULL,
	deleted_by_crm_user_at timestamptz NULL,
	created_by_pharmacy_user_id int4 NULL,
	processed_by_crm_user_id int4 NULL,
	processed_by_crm_user_at timestamptz NULL,
	created_by_crm_user_id int4 NULL,
	created_at timestamptz NOT NULL,
	updated_at timestamptz NOT NULL,
	pharmacy_supplier_id int8 NOT NULL,
	"size" varchar(100) NULL,
	tp_per_unit float8 DEFAULT 0.00 NULL,
	vat_per_unit float8 DEFAULT 0.00 NULL,
	pack_size int4 DEFAULT 1 NULL,
	strength varchar(255) NULL,
	prescription_note text NULL,
	product_details text NULL,
	created_by_supplier_user_id int4 NULL,
	discontinued_at timestamptz NULL,
	discontinued_by_supplier_user_id int4 NULL,
	product_identifier varchar NULL,
	therapeutic_medicine_class_type varchar(50) NULL,
	monograph jsonb DEFAULT '{}'::jsonb NULL,
	is_add_lookup_drug bool DEFAULT false NOT NULL,
	CONSTRAINT product_pkey PRIMARY KEY (id)
);
CREATE INDEX idx_product_deleted_active_updated ON public.product USING btree (is_deleted, is_active, updated_at DESC);
CREATE INDEX idx_product_updated_at_desc ON public.product USING btree (updated_at DESC);
CREATE INDEX product_name_c4c985_idx ON public.product USING btree (name);
CREATE INDEX product_name_idx ON public.product USING btree (name);
CREATE INDEX product_pharmacy_supplier_id_c11b4a2b ON public.product USING btree (pharmacy_supplier_id);
CREATE INDEX product_updated_at_idx ON public.product USING btree (updated_at DESC);


-- public.productupdated definition

-- Drop table

-- DROP TABLE public.productupdated;

CREATE TABLE public.productupdated (
	id int8 NOT NULL,
	"name" text NULL,
	barcode varchar NULL,
	generic_name text NULL,
	"type" varchar(50) NULL,
	origin varchar(50) NULL,
	category varchar NULL,
	sub_category varchar(50) NULL,
	dose varchar NULL,
	image_name text NULL,
	url text NULL,
	image_uploaded_by_user_id int4 NULL,
	is_prescription_required bool NULL,
	is_strip_allowed bool NULL,
	medicine_per_strips int4 NULL,
	cost_per_unit float8 NULL,
	vat float8 NULL,
	new_cost_per_unit float8 NULL,
	rate_per_unit float8 NULL,
	is_approved bool NULL,
	is_active bool NULL,
	is_deleted bool NULL,
	deleted_by_crm_user_id int4 NULL,
	deleted_by_crm_user_at timestamptz NULL,
	created_by_pharmacy_user_id int4 NULL,
	processed_by_crm_user_id int4 NULL,
	processed_by_crm_user_at timestamptz NULL,
	created_by_crm_user_id int4 NULL,
	created_at timestamptz NULL,
	updated_at timestamptz NULL,
	pharmacy_supplier_id int8 NULL,
	"size" varchar(100) NULL,
	tp_per_unit float8 DEFAULT 0.00 NULL,
	vat_per_unit float8 DEFAULT 0.00 NULL,
	pack_size int4 DEFAULT 1 NULL,
	strength varchar(255) NULL,
	prescription_note text NULL,
	product_details text NULL,
	created_by_supplier_user_id int4 NULL,
	discontinued_at timestamptz NULL,
	discontinued_by_supplier_user_id int4 NULL,
	product_identifier varchar NULL,
	therapeutic_medicine_class_type varchar(50) NULL,
	monograph jsonb DEFAULT '{}'::jsonb NULL,
	is_add_lookup_drug bool DEFAULT false NULL,
	CONSTRAINT productupdated_pkey PRIMARY KEY (id)
);